namespace Content.Server.StationEvents.Components;

/// <summary>
///     HardLight: a late-round "Quiet before storm" station event. When it starts it broadcasts an ominous lull,
///     suspends all other scheduled events for <see cref="SuspendMin"/>..<see cref="SuspendMax"/> seconds, then fires
///     a department-gated crisis (anomaly storm / infrastructure failure / asteroid field / invasion / nothing). The
///     crisis sub-events carry their own heat, so the round stays hot afterwards. See
///     <see cref="Content.Server.StationEvents.Events.QuietBeforeStormSystem"/>.
/// </summary>
[RegisterComponent, Access(typeof(Events.QuietBeforeStormSystem))]
public sealed partial class QuietBeforeStormRuleComponent : Component
{
    /// <summary>Minimum lull before the crisis fires, in seconds.</summary>
    [DataField]
    public float SuspendMin = 1200f; // 20 minutes

    /// <summary>Maximum lull before the crisis fires, in seconds.</summary>
    [DataField]
    public float SuspendMax = 2400f; // 40 minutes

    /// <summary>Per-crisis severity score is clamped to at most this value.</summary>
    [DataField]
    public int MaxScore = 5;

    /// <summary>Absolute time at which the crisis resolves.</summary>
    [ViewVariables]
    public TimeSpan CrisisTime;

    [ViewVariables]
    public bool Fired;
}
