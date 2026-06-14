namespace Content.Server.StationEvents.Components;

/// <summary>
///     A round/game-mode property (added as a game rule, e.g. by the adventure preset) that gives the round a
///     non-zero "target" station heat. While current heat is below the target, the event schedulers fire more
///     frequently (up to <c>events.heat_max_frequency_mult</c> times as often) to escalate a too-quiet station.
///     Read by <see cref="Content.Server.StationEvents.StationHeatSystem"/>.
/// </summary>
/// <remarks>
///     If no active rule carries this component the target is 0 (a "relaxed" round) and the frequency boost never
///     applies — i.e. fully back-compatible / opt-in per game mode.
/// </remarks>
[RegisterComponent]
public sealed partial class HeatTargetRuleComponent : Component
{
    /// <summary>
    ///     Target heat gained per hour of round time (smooth, not stepped). The effective target is
    ///     <c>min(HeatPerHour * roundHours, MaxHeat)</c>, so danger ramps up over the early hours and then plateaus.
    /// </summary>
    [DataField]
    public float HeatPerHour = 50f;

    /// <summary>
    ///     The target heat plateaus here. With the default 200 and 50/hour the ramp finishes around the 4-hour mark,
    ///     so the difficulty cap (target + baseline) reaches the heaviest events by roughly the 3rd hour.
    /// </summary>
    [DataField]
    public float MaxHeat = 200f;
}
