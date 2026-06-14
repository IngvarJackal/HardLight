using System;
using Content.Server._NF.Roles.Systems;
using Content.Server.GameTicking;
using Content.Server.StationEvents.Components;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.StationEvents;

/// <summary>
///     Tracks a single, round-scoped "heat" value (measured roughly in minutes-of-chaos) describing how
///     dangerous the station currently is. Heat has two sources:
///     <list type="bullet">
///         <item>A decaying impulse pool, fed by one-shot events (<see cref="EventHeatComponent.Sustained"/> = false).</item>
///         <item>The live sum of <see cref="EventHeatComponent.Cost"/> over all currently active game rules whose
///               <see cref="EventHeatComponent.Sustained"/> is true (ongoing antags such as nukies / xenoborgs).</item>
///     </list>
///     Heat dissipates over time at a rate driven by round length and how much security / command is on station:
///     <c>decayPerMinute = perHour * ceil(roundHours) + perSecurity * securityPlayers + perCommand * commandPlayers</c>.
///     Consumed by <see cref="EventManagerSystem"/> to suppress events the station "can't afford" (whose cost would
///     push current heat past <see cref="CCVars.EventsHeatCeiling"/>) and to bias selection toward danger when quiet.
/// </summary>
public sealed class StationHeatSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly JobTrackingSystem _jobs = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly ProtoId<DepartmentPrototype> SecurityDepartment = "Security";
    private static readonly ProtoId<DepartmentPrototype> CommandDepartment = "Command";

    /// <summary>
    ///     Decaying chaos contributed by one-shot events. Decays toward 0 over time.
    /// </summary>
    private float _impulseHeat;

    // Decay coefficients (heat per minute), pulled from CVars.
    private float _decayPerHour;
    private float _decayPerSecurity;
    private float _decayPerCommand;
    private float _maxFrequencyMultiplier;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.EventsHeatDecayPerHour, value => _decayPerHour = value, true);
        Subs.CVar(_cfg, CCVars.EventsHeatDecayPerSecurity, value => _decayPerSecurity = value, true);
        Subs.CVar(_cfg, CCVars.EventsHeatDecayPerCommand, value => _decayPerCommand = value, true);
        Subs.CVar(_cfg, CCVars.EventsHeatMaxFrequencyMultiplier, value => _maxFrequencyMultiplier = value, true);

        SubscribeLocalEvent<EventHeatComponent, GameRuleStartedEvent>(OnRuleStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRuleStarted(Entity<EventHeatComponent> ent, ref GameRuleStartedEvent args)
    {
        // Sustained rules are counted live while active; only one-shot rules contribute a decaying impulse.
        if (!ent.Comp.Sustained)
            _impulseHeat += ent.Comp.Cost;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _impulseHeat = 0f;
        _suspendUntil = TimeSpan.Zero;
    }

    /// <summary>
    ///     While suspended, <see cref="EventManagerSystem.FindEventWithHeat"/> picks nothing. Used by the
    ///     "Quiet before storm" event to hold an eerie lull before unleashing a crisis.
    /// </summary>
    private TimeSpan _suspendUntil;

    public bool EventsSuspended => _timing.CurTime < _suspendUntil;

    public void SuspendEventsUntil(TimeSpan until)
    {
        if (until > _suspendUntil)
            _suspendUntil = until;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_impulseHeat <= 0f)
            return;

        var decayPerSecond = GetDecayPerMinute() / 60f;
        _impulseHeat = MathF.Max(0f, _impulseHeat - decayPerSecond * frameTime);
    }

    /// <summary>
    ///     The current total station heat: decaying impulse heat plus the live cost of all active sustained rules.
    /// </summary>
    public float CurrentHeat => _impulseHeat + GetSustainedHeat();

    /// <summary>
    ///     How fast heat currently dissipates, in heat units per minute. Scales with round length and with the
    ///     number of active security / command crew (who are expected to handle chaos).
    /// </summary>
    public float GetDecayPerMinute()
    {
        var hours = (float) Math.Ceiling(_gameTicker.RoundDuration().TotalHours);
        var security = CountDepartmentPlayers(SecurityDepartment);
        var command = CountDepartmentPlayers(CommandDepartment);

        return _decayPerHour * hours + _decayPerSecurity * security + _decayPerCommand * command;
    }

    /// <summary>
    ///     The round's target heat, a game-mode property driving difficulty escalation and the frequency boost. It is
    ///     a smooth ramp <c>min(HeatPerHour * elapsedHours, MaxHeat)</c> using the largest values among active
    ///     <see cref="HeatTargetRuleComponent"/> rules, so danger rises over the early hours and then plateaus.
    ///     0 if no such rule is active (a "relaxed" round: no difficulty gate or frequency boost).
    /// </summary>
    public float TargetHeat
    {
        get
        {
            var perHour = 0f;
            var maxHeat = 0f;
            var query = EntityQueryEnumerator<HeatTargetRuleComponent, ActiveGameRuleComponent>();
            while (query.MoveNext(out _, out var target, out _))
            {
                if (target.HeatPerHour > perHour)
                    perHour = target.HeatPerHour;
                if (target.MaxHeat > maxHeat)
                    maxHeat = target.MaxHeat;
            }

            if (perHour <= 0f)
                return 0f;

            // Smooth ramp over the early hours, then plateau at MaxHeat.
            var elapsed = (float) _gameTicker.RoundDuration().TotalHours;
            var target2 = perHour * elapsed;
            return maxHeat > 0f ? MathF.Min(target2, maxHeat) : target2;
        }
    }

    /// <summary>
    ///     How much faster the schedulers should fire right now. 1.0 when heat is at/above the round's target (or no
    ///     target is set), rising linearly toward <c>events.heat_max_frequency_mult</c> as heat falls to 0. Schedulers
    ///     should divide their next-event interval by this value.
    /// </summary>
    public float GetFrequencyMultiplier()
    {
        var target = TargetHeat;
        if (target <= 0f)
            return 1f;

        var deficit = MathF.Max(0f, target - CurrentHeat);
        var ratio = MathF.Min(1f, deficit / target); // 0 (at/above target) .. 1 (heat is 0)
        return 1f + ratio * (_maxFrequencyMultiplier - 1f);
    }

    private int CountDepartmentPlayers(ProtoId<DepartmentPrototype> departmentId)
    {
        if (!_proto.TryIndex(departmentId, out var department))
            return 0;

        var count = 0;
        foreach (var role in department.Roles)
        {
            count += _jobs.GetNumberOfActiveRoles(role);
        }

        return count;
    }

    private float GetSustainedHeat()
    {
        var total = 0f;
        var query = EntityQueryEnumerator<EventHeatComponent, ActiveGameRuleComponent>();
        while (query.MoveNext(out _, out var heat, out _))
        {
            if (heat.Sustained && !heat.Released)
                total += heat.Cost;
        }

        return total;
    }

    /// <summary>
    ///     Stops a sustained rule from holding heat as a fixed floor and instead dumps its cost into the decaying
    ///     impulse pool, so the round's heat starts naturally falling. Used when an overt threat is "revealed"
    ///     (some time after the sector threat alert) so it can no longer stall the round indefinitely.
    /// </summary>
    public void ReleaseSustained(Entity<EventHeatComponent> ent)
    {
        if (!ent.Comp.Sustained || ent.Comp.Released)
            return;

        ent.Comp.Released = true;
        _impulseHeat += ent.Comp.Cost;
    }
}
