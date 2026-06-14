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

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.EventsHeatDecayPerHour, value => _decayPerHour = value, true);
        Subs.CVar(_cfg, CCVars.EventsHeatDecayPerSecurity, value => _decayPerSecurity = value, true);
        Subs.CVar(_cfg, CCVars.EventsHeatDecayPerCommand, value => _decayPerCommand = value, true);

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
            if (heat.Sustained)
                total += heat.Cost;
        }

        return total;
    }
}
