using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Roles.Components;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Enums; // HardLight
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Hardlight.StationPay;

[UsedImplicitly]
public sealed class StationPaySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public int PayoutDelay { get; private set; }

    // map of {Mind.OwnedEntity: lastPayoutTime} where lastPayoutTime was the round duration at time of payout
    // sorted in ascending order
    private readonly Dictionary<ProtoId<JobPrototype>, int> _jobPayoutRates = new();
    private OrderedDictionary<EntityUid, int> _scheduledPayouts = new();
    private bool _roundEndProcessed; // ensure payouts run once per round
    private bool _pendingRoundStartResync; // HardLight

    public override void Initialize()
    {
        base.Initialize();

        foreach (var proto in _prototypeManager.EnumeratePrototypes<StationPayPrototype>())
        {
            _jobPayoutRates[proto.JobProto] = proto.PayPerHour;
            //Log.Debug($"[stationpay] loaded prototype: {proto.JobProto.Id} at {proto.PayPerHour}");
        }

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAddedEvent);
        SubscribeLocalEvent<RoleRemovedEvent>(OnRoleRemovedEvent);
        // Keep payout scheduling aligned across mind transfers between bodies; RoleAdded/RoleRemoved
        // do not fire when a mind moves between entities, so the schedule could be stranded on the
        // old body and silently fail. JobTrackingSystem already owns the directed
        // <JobTrackingComponent, MindAddedMessage/MindRemovedMessage> subscription, so we hook the
        // broadcast variant and gate on the component ourselves.
        SubscribeLocalEvent<MindAddedMessage>(OnAnyMindAdded);
        SubscribeLocalEvent<MindRemovedMessage>(OnAnyMindRemoved);

        /*
         * TODO: account for disconnecting players
         *
         * when someone disconnects add them to a removal list with a timestamp 10 minutes in the future
         *
         * after that time they are removed from the scheduledpayout dict
         *
         * if they reconnect before that time they are removed from the disconnect tracker
         *
         * this allows for a grace period where if you happen to disconnect right before the hour you still get paid
         *
         * and if you disconnect and reconnect you still get paid
         *
         * we also don't have to do any complex bookkeeping
         */
        // SubscribeLocalEvent<MindRemovedMessage>(OnMindRemoved);
        Subs.CVar(_cfg, CCVars.GameStationPayoutDelay, time => PayoutDelay = (int)time.TotalSeconds, true);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        // Reset idempotency flag when a new round starts
        if (ev.New == GameRunLevel.InRound)
        {
            _roundEndProcessed = false;
            _pendingRoundStartResync = true; // HardLight
            return;
        }

        // Handle end-of-round transitions; ensure we only process once
        var isEndTransition =
            (ev.Old == GameRunLevel.InRound && ev.New == GameRunLevel.PreRoundLobby) // restartroundnow edge case
            || (ev.New == GameRunLevel.PostRound);

        if (!isEndTransition || _roundEndProcessed)
            return;

        OnRoundEnd();
        _roundEndProcessed = true;
    }

    private void OnRoundEnd()
    {
        var now = (int)_gameTicker.RoundDuration().TotalSeconds;

        // payout anyone who worked less than an hour at round end
        foreach (var (uid, lastPayout) in _scheduledPayouts)
        {
            PayoutFor(uid, now - lastPayout);
        }

        _scheduledPayouts.Clear();
    }

    private bool GetJobForEntity(
        [NotNullWhen(true)] EntityUid? uid,
        [NotNullWhen(true)] out ProtoId<JobPrototype>? jobPrototype)
    {
        jobPrototype = null;
        if (TryComp<JobTrackingComponent>(uid, out var jtc)
            && jtc.Job is {} job
            && _jobPayoutRates.ContainsKey(job))
        {
            jobPrototype = job;
        }

        return jobPrototype != null;
    }

    private void OnRoleAddedEvent(RoleAddedEvent args)
    {
        var uid = args.Mind.OwnedEntity;

        if (uid == null
            || !TryComp<BankAccountComponent>(uid, out _)
            || !GetJobForEntity(uid, out _) // HardLight: out var job<out _
           )
        {
            //Log.Debug($"[stationpay] Character {args.Mind.CharacterName} joined but was not valid for station pay");
            return;
        }

        TrySchedulePayout(uid.Value); // HardLight
    }

    // HardLight: Rebuild payout schedules at round start for valid in-game entities that may have persisted across transitions.
    private void ResyncScheduledPayoutsForCurrentRound()
    {
        var query = EntityQueryEnumerator<JobTrackingComponent, BankAccountComponent, MindContainerComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var mindContainer))
        {
            if (!mindContainer.HasMind)
                continue;

            if (!TryComp<MindComponent>(mindContainer.Mind.Value, out var mind))
                continue;

            if (!_player.TryGetSessionById(mind.UserId, out var session)
                || session.Status != SessionStatus.InGame
                || session.AttachedEntity != uid)
                continue;

            if (!GetJobForEntity(uid, out _))
                continue;

            TrySchedulePayout(uid);
        }
    }

    // HardLight: Idempotent scheduling helper shared by role-added flow and round-start resync.
    private void TrySchedulePayout(EntityUid uid)
    {
        // if they already have a scheduled payout, we don't need to do anything
        if (_scheduledPayouts.ContainsKey(uid))
            return;

        // schedule their first payout from now.
        _scheduledPayouts.Insert(
            _scheduledPayouts.Count,
            uid,
            (int)_gameTicker.RoundDuration().TotalSeconds + PayoutDelay
        );
    }

    private void OnRoleRemovedEvent(RoleRemovedEvent args)
    {
        if (args.Mind.OwnedEntity == null)
            return;

        //Log.Debug($"[stationpay] Character {args.Mind.CharacterName}'s job was removed");
        _scheduledPayouts.Remove((EntityUid)args.Mind.OwnedEntity);
    }

    // Re-schedule payouts when a mind moves into a job-tracked body (mid-round body swap, cloning, etc.).
    // Mirrors the OnRoleAddedEvent gating but uses the in-game-attached session as the authoritative check.
    private void OnAnyMindAdded(MindAddedMessage args)
    {
        var uid = args.Container.Owner;
        if (!HasComp<JobTrackingComponent>(uid)
            || !HasComp<BankAccountComponent>(uid)
            || !GetJobForEntity(uid, out _)
            || !_player.TryGetSessionById(args.Mind.Comp.UserId, out var session)
            || session.Status != SessionStatus.InGame
            || session.AttachedEntity != uid)
        {
            return;
        }

        TrySchedulePayout(uid);
    }

    // Drop schedule entries for bodies that are no longer minded so the Update loop doesn't
    // tight-loop retrying payouts against an entity that can never receive one.
    private void OnAnyMindRemoved(MindRemovedMessage args)
    {
        _scheduledPayouts.Remove(args.Container.Owner);
    }

    private bool PayoutFor(EntityUid uid, int secondsWorked)
    {
        if (!_scheduledPayouts.ContainsKey(uid))
        {
            //Log.Debug($"[stationpay] Attemped payout for {uid}, but no scheduled payout was found");
            return false;
        }

        if (!GetJobForEntity(uid, out var jobId))
        {
            //Log.Debug($"[stationpay] Attemped payout for {uid}, but no valid job found");
            return false;
        }

        var employedTime = (int)(secondsWorked / (double)PayoutDelay);

        // this could in principle be 0 if someone joined right before round end
        if (employedTime <= 0)
        {
            //Log.Debug($"[stationpay] Skipping payout for {uid} due to employedTime <= 0 (secondsWorked: {secondsWorked})");
            return false;
        }

        // Don't deposit if there's no in-game session attached to this body. Returning false here
        // (instead of true) leaves the schedule unadvanced so the missed interval is retried once
        // the player is back in-game, rather than silently dropped.
        if (!_player.TryGetSessionByEntity(uid, out var session)
            || session.Status != SessionStatus.InGame)
        {
            return false;
        }

        var rate = _jobPayoutRates[(ProtoId<JobPrototype>)jobId];
        var amount = employedTime * rate;
        //Log.Info($"Paying entity {uid} ${amount} for {secondsWorked} seconds of work as {jobId.Value.Id}.");

        if (_bank.TryBankDeposit(uid, amount))
        {
            var job = _prototypeManager.Index<JobPrototype>(jobId);
            var message = Loc.GetString("stationpay-notify-payment",
                ("pay", amount),
                ("time", secondsWorked / 60),
                ("job", job.LocalizedName)
            );
            var wrappedMessage = Loc.GetString("pda-notification-message",
                ("header", Loc.GetString("stationpay-notify-pda-header")),
                ("message", message));

            _chat.ChatMessageToOne(ChatChannel.Notifications,
                message,
                wrappedMessage,
                EntityUid.Invalid,
                false,
                session.Channel);

            return true;
        }

        return false;
        /* else
            Log.Error("[stationpay] Failed to deposit station pay for uid: " + uid); */
    }

    // HardLight: throttle accumulator. Payouts are tracked in whole seconds, so checking
    // every second instead of every tick is gameplay-neutral.
    private float _payoutCheckAccumulator;
    private const float PayoutCheckInterval = 1f;

    // Scratch buffers reused across Update calls to avoid per-tick allocation.
    // Holds (uid, oldScheduledTime) for entries due this pass.
    private readonly List<(EntityUid Uid, int Scheduled)> _duePayoutsScratch = new();

    public override void Update(float frameTime)
    {
        // HardLight: Run round-start resync once after entering InRound to restore missed schedules.
        if (_pendingRoundStartResync)
        {
            _pendingRoundStartResync = false;
            ResyncScheduledPayoutsForCurrentRound();
        }

        _payoutCheckAccumulator += frameTime;
        if (_payoutCheckAccumulator < PayoutCheckInterval)
        {
            base.Update(frameTime);
            return;
        }
        _payoutCheckAccumulator = 0f;

        if (_scheduledPayouts.Count == 0)
        {
            base.Update(frameTime);
            return;
        }

        var now = (int)_gameTicker.RoundDuration().TotalSeconds;

        // _scheduledPayouts is maintained in ascending order by scheduled time, so if the
        // earliest entry is in the future, nothing is due.
        if (_scheduledPayouts.GetAt(0).Value > now)
        {
            base.Update(frameTime);
            return;
        }

        // Collect due entries; bail out at the first not-due entry (sorted ascending).
        _duePayoutsScratch.Clear();
        foreach (var (uid, scheduledPayoutTime) in _scheduledPayouts)
        {
            if (scheduledPayoutTime > now)
                break;
            _duePayoutsScratch.Add((uid, scheduledPayoutTime));
        }

        // Remove all due entries; re-add at scheduledPayoutTime + PayoutDelay (matches prior semantics,
        // including catch-up payouts when scheduled times are in the past). Since the not-due tail is
        // already sorted ascending and the rescheduled times are also in ascending order
        // (oldScheduled was ascending, and we add a constant), a 2-way merge into the OrderedDictionary
        // preserves the global ascending invariant. The simplest correct approach: drop them all,
        // pay each, then insert each rescheduled entry at the right position via Insert.
        for (var i = 0; i < _duePayoutsScratch.Count; i++)
            _scheduledPayouts.Remove(_duePayoutsScratch[i].Uid);

        for (var i = 0; i < _duePayoutsScratch.Count; i++)
        {
            var (uid, oldScheduled) = _duePayoutsScratch[i];
            // Pay first, then decide whether to advance the schedule. If payout failed (e.g. player
            // briefly offline / not yet attached), keep the original scheduled time so the interval
            // is retried on the next Update tick instead of silently consumed.
            var payoutSucceeded = PayoutFor(uid, PayoutDelay);
            var newScheduled = payoutSucceeded ? oldScheduled + PayoutDelay : oldScheduled;

            // Find insertion index in ascending order. Most rescheduled entries land at the end,
            // so search from the back for amortized O(1).
            var insertIdx = _scheduledPayouts.Count;
            while (insertIdx > 0 && _scheduledPayouts.GetAt(insertIdx - 1).Value > newScheduled)
                insertIdx--;
            _scheduledPayouts.Insert(insertIdx, uid, newScheduled);
        }

        base.Update(frameTime);
    }
}
