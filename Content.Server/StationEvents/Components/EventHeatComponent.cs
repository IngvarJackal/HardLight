namespace Content.Server.StationEvents.Components;

/// <summary>
///     Optional, fully back-compatible "danger budget" data for a game rule (a station event or an antag rule).
///     Read by <see cref="Content.Server.StationEvents.StationHeatSystem"/> to track how chaotic the round
///     currently is, and by <see cref="EventManagerSystem"/> to bias/suppress which events the schedulers pick.
/// </summary>
/// <remarks>
///     If this component is absent (or <see cref="Cost"/> is 0) the rule is invisible to the heat system and
///     behaves exactly as it did before this feature existed.
/// </remarks>
[RegisterComponent]
public sealed partial class EventHeatComponent : Component
{
    /// <summary>
    ///     How much "heat" (chaos / danger) this rule represents, measured roughly in minutes-of-chaos. Higher = more
    ///     disruptive. Suggested scale (with the default ceiling of 300): 0 = harmless flavor, ~30-70 = minor/standard
    ///     disruption, ~100-150 = serious threat (ninja, dragon, midround traitors), ~180-250 = round-defining overt
    ///     threat (nukies, xenoborgs, zombie outbreak).
    /// </summary>
    [DataField]
    public float Cost;

    /// <summary>
    ///     If true, <see cref="Cost"/> is contributed continuously for as long as the rule is active
    ///     (use for ongoing antags whose game rule persists, e.g. Nukeops, dragons, sleeper agents).
    ///     If false (default), <see cref="Cost"/> is injected once as a decaying impulse when the rule starts
    ///     (use for one-shot environmental events like gas leaks or meteor swarms).
    /// </summary>
    [DataField]
    public bool Sustained;
}
