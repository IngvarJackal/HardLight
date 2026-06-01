using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Objectives.Components;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server.Objectives.Systems;

/// <summary>
/// Rerolls kill/maroon objectives whose target has left the round (cryo, ghost, disconnect, cleanup)
/// while the traitor has made 0% progress against them.
/// </summary>
public sealed class ObjectiveTargetRefreshSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly ObjectivesSystem _objectives = default!;
    [Dependency] private readonly TargetObjectiveSystem _target = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        // Run after MindSystem so the mind's OwnedEntity is already cleared (victory state settled)
        // before we decide whether a kill/maroon objective should be rerolled.
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating, after: new[] { typeof(MindSystem) });
    }

    /// <summary>
    /// Fires when a player session leaves any entity (ghosting, cryo, body swap).
    /// By this point the mind's OwnedEntity is already updated to the ghost or new body,
    /// so we can tell whether the player truly left the round.
    /// </summary>
    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        // Ignore session leaving a ghost — only care about leaving a real character body.
        if (HasComp<GhostComponent>(ev.Entity))
            return;

        if (!_mind.TryGetMind(ev.Entity, out var mindId, out var mind))
            return;

        // Only reroll if the mind is now bodyless or stuck in a ghost (not a fresh clone / new body).
        if (mind.OwnedEntity != null && !HasComp<GhostComponent>(mind.OwnedEntity.Value))
            return;

        HandleTargetLeft(mindId);
    }

    /// <summary>
    /// Fallback for disconnected players whose body entity is deleted by admin or timer cleanup.
    /// Connected players always auto-ghost first and are caught by <see cref="OnPlayerDetached"/>.
    /// </summary>
    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        var uid = args.Entity.Owner;

        // Only care about entities that can hold a mind.
        if (!HasComp<MindContainerComponent>(uid))
            return;

        // Ghost deletion is already handled (or already triggered) via OnPlayerDetached.
        if (HasComp<GhostComponent>(uid))
            return;

        if (!_mind.TryGetMind(uid, out var mindId, out var mind))
            return;

        // Skip connected players — they will auto-ghost and be handled by OnPlayerDetached.
        if (mind.UserId != null && _playerManager.ValidSessionId(mind.UserId.Value))
            return;

        HandleTargetLeft(mindId);
    }

    private void HandleTargetLeft(EntityUid targetMindId)
    {
        var targetName = TryComp<MindComponent>(targetMindId, out var targetMind) ? targetMind.CharacterName ?? "Unknown" : "Unknown";
        Log.Info($"ObjectiveTargetRefresh: target mind {ToPrettyString(targetMindId)} ({targetName}) left the round, scanning traitor objectives for rerolls.");

        var traitorQuery = EntityQueryEnumerator<TraitorRuleComponent, GameRuleComponent>();
        while (traitorQuery.MoveNext(out var ruleUid, out var traitorComp, out var gameRule))
        {
            if (!_gameTicker.IsGameRuleActive(ruleUid, gameRule))
                continue;

            foreach (var traitorMindId in traitorComp.TraitorMinds)
            {
                if (traitorMindId == targetMindId)
                    continue;

                if (!TryComp<MindComponent>(traitorMindId, out var traitorMind))
                    continue;

                // Iterate in reverse so removal by index stays valid.
                for (var i = traitorMind.Objectives.Count - 1; i >= 0; i--)
                {
                    var objUid = traitorMind.Objectives[i];

                    if (!TryComp<KillPersonConditionComponent>(objUid, out _))
                        continue;

                    if (!_target.GetTarget(objUid, out var currentTarget))
                        continue;

                    if (currentTarget.Value != targetMindId)
                        continue;

                    // Don't reroll if the traitor already has any progress (partial kills, etc.).
                    var progress = _objectives.GetProgress(objUid, (traitorMindId, traitorMind));
                    if (progress is null or >= 0.01f)
                    {
                        Log.Info($"ObjectiveTargetRefresh: traitor {ToPrettyString(traitorMindId)} has progress {progress} on objective {ToPrettyString(objUid)} targeting the leaver, skipping reroll.");
                        continue;
                    }

                    Log.Info($"ObjectiveTargetRefresh: traitor {ToPrettyString(traitorMindId)} has objective {ToPrettyString(objUid)} targeting the leaver with no progress, rerolling.");
                    TryRerollObjectiveTarget(objUid, i, traitorMindId, traitorMind);
                }
            }
        }
    }

    private void TryRerollObjectiveTarget(EntityUid objUid, int objIndex, EntityUid traitorMindId, MindComponent traitorMind)
    {
        if (!TryComp<PickRandomPersonComponent>(objUid, out var pickComp))
        {
            // Can't pick a new target without a pool — remove the unachievable objective.
            Log.Info($"ObjectiveTargetRefresh: objective {ToPrettyString(objUid)} has no PickRandomPersonComponent pool, removing it from traitor {ToPrettyString(traitorMindId)}.");
            _mind.TryRemoveObjective(traitorMindId, traitorMind, objIndex);
            NotifyTraitor(traitorMind, Loc.GetString("objective-target-refresh-removed"));
            return;
        }

        var newTarget = _mind.PickFromPool(pickComp.Pool, pickComp.Filters, traitorMindId);

        if (newTarget == null)
        {
            // No valid replacement found — remove the objective rather than leave it stuck.
            Log.Info($"ObjectiveTargetRefresh: no valid replacement target found for objective {ToPrettyString(objUid)}, removing it from traitor {ToPrettyString(traitorMindId)}.");
            _mind.TryRemoveObjective(traitorMindId, traitorMind, objIndex);
            NotifyTraitor(traitorMind, Loc.GetString("objective-target-refresh-removed"));
            return;
        }

        _target.SetTarget(objUid, newTarget.Value);

        // Refresh the entity name on the objective to match the new target.
        var afterEv = new ObjectiveAfterAssignEvent(traitorMindId, traitorMind, Comp<ObjectiveComponent>(objUid), MetaData(objUid));
        RaiseLocalEvent(objUid, ref afterEv);

        var newName = TryComp<MindComponent>(newTarget.Value, out var newMind) ? newMind.CharacterName ?? "Unknown" : "Unknown";
        Log.Info($"ObjectiveTargetRefresh: rerolled objective {ToPrettyString(objUid)} for traitor {ToPrettyString(traitorMindId)} to new target {ToPrettyString(newTarget.Value)} ({newName}).");
        NotifyTraitor(traitorMind, Loc.GetString("objective-target-refresh-rerolled", ("name", newName)));
    }

    private void NotifyTraitor(MindComponent traitorMind, string message)
    {
        if (traitorMind.UserId == null)
            return;

        if (!_playerManager.TryGetSessionById(traitorMind.UserId.Value, out var session))
            return;

        _chatManager.DispatchServerMessage(session, message);
    }
}
