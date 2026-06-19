using System;
using Content.Server.StationEvents.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents.Events;

/// <summary>
///     HardLight: drives the standalone "anomaly storm" event (split out of the old Quiet-before-storm crisis).
///     Announces on start as its one-minute warning and applies its heat immediately like any event; when the
///     warning elapses it fires a Science-headcount-scaled burst of anomaly spawns plus its fixed side events.
/// </summary>
public sealed class AnomalyStormSystem : StationEventSystem<AnomalyStormRuleComponent>
{
    [Dependency] private readonly EventScalingSystem _scaling = default!;

    // HardLight: the storm scales off the science department's live headcount (research assistants excluded).
    // Same pattern as VentHordeRule — reuses EventScalingSystem with a different department.
    private static readonly ProtoId<DepartmentPrototype> ScalingDepartment = "Science";
    private static readonly ProtoId<JobPrototype> ScalingInternJob = "ResearchAssistant";

    protected override void Ended(EntityUid uid, AnomalyStormRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        var headcount = _scaling.DepartmentHeadcount(ScalingDepartment, ScalingInternJob);
        var score = Math.Clamp(1 + headcount, 1, component.MaxScore);

        for (var i = 0; i < score; i++)
            GameTicker.StartGameRule(component.AnomalyRule);

        foreach (var rule in component.SideRules)
            GameTicker.StartGameRule(rule);
    }
}
