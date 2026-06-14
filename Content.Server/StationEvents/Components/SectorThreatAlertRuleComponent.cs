using Robust.Shared.Audio;

namespace Content.Server.StationEvents.Components;

/// <summary>
///     HardLight: added to a round-start antag game rule (nukies, xenoborgs, wizard) so that, after a delay,
///     Central Command broadcasts a "passive sector threat detection" announcement hinting at the antag's possible
///     presence. Prevents overt antags from stalling indefinitely while keeping the reveal vague and non-confirming.
/// </summary>
[RegisterComponent, Access(typeof(SectorThreatAlertSystem))]
public sealed partial class SectorThreatAlertRuleComponent : Component
{
    /// <summary>
    ///     Loc id of the vague threat-detection announcement broadcast to the whole sector.
    /// </summary>
    [DataField(required: true)]
    public LocId Announcement;

    /// <summary>
    ///     Loc id of the announcement sender shown to players.
    /// </summary>
    [DataField]
    public LocId Sender = "sector-threat-alert-sender";

    /// <summary>
    ///     Earliest time after the rule starts that the timer-based alert can fire, in seconds.
    /// </summary>
    [DataField]
    public float MinDelay = 3600f; // 60 minutes

    /// <summary>
    ///     Latest time after the rule starts that the timer-based alert can fire, in seconds.
    /// </summary>
    [DataField]
    public float MaxDelay = 5400f; // 90 minutes

    /// <summary>
    ///     If true, the alert is NOT scheduled on a timer at round start; instead it is scheduled
    ///     <see cref="ZombifyTriggerDelay"/> seconds after the first initial infected zombifies. Used for the
    ///     zombie outbreak, whose reveal is guaranteed to happen.
    /// </summary>
    [DataField]
    public bool TriggerOnZombified;

    /// <summary>
    ///     Delay after the first initial infected zombifies before the alert fires, in seconds. Only used when
    ///     <see cref="TriggerOnZombified"/> is true.
    /// </summary>
    [DataField]
    public float ZombifyTriggerDelay = 900f; // 15 minutes

    /// <summary>
    ///     Delay after the alert fires before this rule's sustained heat (if any) is released to decay, in seconds.
    /// </summary>
    [DataField]
    public float HeatReleaseDelay = 1800f; // 30 minutes

    [DataField]
    public Color Color = Color.OrangeRed;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

    /// <summary>
    ///     Absolute game time at which the alert fires. Unset (default) until scheduled.
    /// </summary>
    [ViewVariables]
    public TimeSpan AlertTime;

    /// <summary>
    ///     True once <see cref="AlertTime"/> has been chosen (timer rolled, or zombify trigger seen).
    /// </summary>
    [ViewVariables]
    public bool Scheduled;

    [ViewVariables]
    public bool Announced;

    /// <summary>
    ///     True once this rule's sustained heat has been released to decay.
    /// </summary>
    [ViewVariables]
    public bool HeatReleased;
}
