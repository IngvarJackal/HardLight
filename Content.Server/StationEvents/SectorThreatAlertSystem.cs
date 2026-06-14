using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server.StationEvents.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Zombies;

namespace Content.Server.StationEvents;

/// <summary>
///     HardLight: fires a delayed, vague Central Command "passive sector threat detection" announcement for an overt
///     threat (see <see cref="SectorThreatAlertRuleComponent"/>), then — after a further delay — releases the rule's
///     sustained heat so it stops stalling the round. This replaces fragile per-antag "defeat detection": the threat
///     is always revealed on a timer (nukies/xenoborgs, 60-90 min) or off a guaranteed event (zombies, 15 min after
///     the first initial infected turns), and its heat then decays normally.
/// </summary>
public sealed class SectorThreatAlertSystem : GameRuleSystem<SectorThreatAlertRuleComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly StationHeatSystem _heat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntityZombifiedEvent>(OnZombified);
    }

    protected override void Started(EntityUid uid, SectorThreatAlertRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Timer-based threats schedule immediately; zombify-triggered ones wait for the first turn.
        if (component.TriggerOnZombified)
            return;

        component.AlertTime = Timing.CurTime + TimeSpan.FromSeconds(RobustRandom.NextFloat(component.MinDelay, component.MaxDelay));
        component.Scheduled = true;
    }

    private void OnZombified(ref EntityZombifiedEvent args)
    {
        if (!HasComp<InitialInfectedComponent>(args.Target))
            return;

        var query = EntityQueryEnumerator<SectorThreatAlertRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var component, out var gameRule))
        {
            if (!component.TriggerOnZombified || component.Scheduled || !GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            component.AlertTime = Timing.CurTime + TimeSpan.FromSeconds(component.ZombifyTriggerDelay);
            component.Scheduled = true;
        }
    }

    protected override void ActiveTick(EntityUid uid, SectorThreatAlertRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (!component.Scheduled)
            return;

        if (!component.Announced)
        {
            if (Timing.CurTime < component.AlertTime)
                return;

            component.Announced = true;
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString(component.Announcement),
                Loc.GetString(component.Sender),
                playSound: true,
                announcementSound: component.Sound,
                colorOverride: component.Color);
            return;
        }

        // Some time after the reveal, let this threat's sustained heat decay so it can't stall the round forever.
        if (!component.HeatReleased && Timing.CurTime >= component.AlertTime + TimeSpan.FromSeconds(component.HeatReleaseDelay))
        {
            component.HeatReleased = true;
            if (TryComp<EventHeatComponent>(uid, out var eventHeat))
                _heat.ReleaseSustained((uid, eventHeat));
        }
    }
}
