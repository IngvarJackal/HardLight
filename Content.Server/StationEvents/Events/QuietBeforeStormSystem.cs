using System;
using System.Collections.Generic;
using Content.Server._NF.Roles.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server.StationEvents.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.StationEvents.Events;

/// <summary>
///     HardLight: drives the "Quiet before storm" event. On start it suspends the schedulers for a lull, then resolves
///     into one department-gated crisis. The crisis is only valid if its department has at least 2 non-trainee crew;
///     if none qualify it falls back to a "nothingburger" (the suspense simply lifts). Severity scales with that
///     department's headcount. The rule carries no heat of its own; all heat comes from the crisis sub-events it
///     fires directly (bypassing affordability), so the round stays hot afterwards — or none, for the nothingburger.
/// </summary>
public sealed class QuietBeforeStormSystem : StationEventSystem<QuietBeforeStormRuleComponent>
{
    [Dependency] private readonly StationHeatSystem _heat = default!;
    [Dependency] private readonly JobTrackingSystem _jobs = default!;

    // Trainee/intern roles excluded from the crisis severity score.
    private static readonly ProtoId<DepartmentPrototype> Science = "Science";
    private static readonly ProtoId<DepartmentPrototype> Engineering = "Engineering";
    private static readonly ProtoId<DepartmentPrototype> Security = "Security";
    private static readonly ProtoId<JobPrototype> ScienceIntern = "ResearchAssistant";
    private static readonly ProtoId<JobPrototype> EngineeringIntern = "TechnicalAssistant";
    private static readonly ProtoId<JobPrototype> SecurityIntern = "SecurityCadet";

    // Base vent-critter event ids. Each has a "lone critter" variant (this id, a VentCrittersRule spawn) and a
    // matching horde variant (this id + "Horde", a VentHordeRule swarm). Invasion fires both of the same type.
    private const string HordeSuffix = "Horde";
    private static readonly string[] InvasionCritters =
    {
        "Punkvents", "Mercvents", "Explorervents", "ArgocyteVents", "XenoVents", "AiVents",
        "SpaceVents", "CarpVents", "TickVents", "Cultvents", "Syndivents", "Zedvents", "LizardVents",
        "Dinosaurvents", "Fleshvents",
    };

    protected override void Started(EntityUid uid, QuietBeforeStormRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args); // dispatches the ominous StationEvent.startAnnouncement

        var delay = RobustRandom.NextFloat(component.SuspendMin, component.SuspendMax);
        component.CrisisTime = Timing.CurTime + TimeSpan.FromSeconds(delay);
        _heat.SuspendEventsUntil(component.CrisisTime);
    }

    protected override void ActiveTick(EntityUid uid, QuietBeforeStormRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (component.Fired || Timing.CurTime < component.CrisisTime)
            return;

        component.Fired = true;
        ResolveCrisis(component);
        GameTicker.EndGameRule(uid, gameRule);
    }

    private void ResolveCrisis(QuietBeforeStormRuleComponent component)
    {
        var sci = DepartmentHeadcount(Science, ScienceIntern);
        var engi = DepartmentHeadcount(Engineering, EngineeringIntern);
        var sec = DepartmentHeadcount(Security, SecurityIntern);

        var options = new List<Action>();
        if (sci >= 2)
            options.Add(() => AnomalyStorm(Score(sci, component)));
        if (engi >= 2)
        {
            options.Add(() => InfrastructureFailure(Score(engi, component)));
            options.Add(() => AsteroidField(Score(engi, component)));
        }
        if (sec >= 2)
            options.Add(() => Invasion(Score(sec, component)));

        // Nothingburger: always possible, and the only outcome when nothing else qualifies.
        options.Add(Nothingburger);

        RobustRandom.Pick(options).Invoke();
    }

    private int Score(int headcount, QuietBeforeStormRuleComponent component)
        => Math.Clamp(1 + headcount, 1, component.MaxScore);

    private int DepartmentHeadcount(ProtoId<DepartmentPrototype> departmentId, ProtoId<JobPrototype> intern)
    {
        if (!PrototypeManager.TryIndex(departmentId, out var department))
            return 0;

        var count = 0;
        foreach (var role in department.Roles)
        {
            if (role == intern)
                continue;
            count += _jobs.GetNumberOfActiveRoles(role);
        }

        return count;
    }

    private void Fire(string ruleId, int count = 1)
    {
        for (var i = 0; i < count; i++)
            GameTicker.StartGameRule(ruleId);
    }

    private void AnomalyStorm(int score)
    {
        Fire("AnomalySpawn", score);
        Fire("NoosphericStorm");
        Fire("GlimmerWispSpawn");
    }

    private void InfrastructureFailure(int score)
    {
        Fire("IonStorm");
        Fire("SolarFlare");
        Fire("GasLeak", score);
        Fire("BreakerFlip");
        Fire("VentClog", score);
        Fire("GreytideVirus");
    }

    private void AsteroidField(int score)
    {
        Fire("GameRuleMeteorSwarmMedium", score);
    }

    private void Invasion(int score)
    {
        // Pick one critter type and split the spawns 50/50 between its horde and lone-critter variants, giving the
        // remainder to the (weaker) lone critters. Same type for both, e.g. slime hordes alongside slime critters.
        var critter = RobustRandom.Pick(InvasionCritters);
        var hordes = score / 2;
        var lone = score - hordes; // gets the remainder, so lone >= hordes
        Fire(critter + HordeSuffix, hordes);
        Fire(critter, lone);
    }

    private void Nothingburger()
    {
        // Intentionally empty: the suspense lifts and nothing comes of it.
    }
}
