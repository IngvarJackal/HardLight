using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents.Components;

/// <summary>
///     HardLight: a standalone "anomaly storm" station event (split out of the old Quiet-before-storm crisis).
///     When its warning elapses it fires a burst of anomaly spawns scaled by the Science department's headcount,
///     plus a noospheric storm and a glimmer-wisp swarm. Carries its own heat. See
///     <see cref="Content.Server.StationEvents.Events.AnomalyStormSystem"/>.
/// </summary>
[RegisterComponent, Access(typeof(Events.AnomalyStormSystem))]
public sealed partial class AnomalyStormRuleComponent : Component
{
    /// <summary>Department whose headcount scales the storm.</summary>
    [DataField]
    public ProtoId<DepartmentPrototype> Department = "Science";

    /// <summary>Trainee role excluded from the headcount.</summary>
    [DataField]
    public ProtoId<JobPrototype>? InternJob = "ResearchAssistant";

    /// <summary>Severity (number of anomaly spawns) is clamped to at most this.</summary>
    [DataField]
    public int MaxScore = 5;

    /// <summary>Anomaly spawn rule, fired <c>clamp(1 + headcount, 1, MaxScore)</c> times.</summary>
    [DataField]
    public EntProtoId AnomalyRule = "AnomalySpawn";

    /// <summary>One-shot side events fired once alongside the anomaly burst.</summary>
    [DataField]
    public List<EntProtoId> SideRules = new() { "NoosphericStorm", "GlimmerWispSpawn" };
}
