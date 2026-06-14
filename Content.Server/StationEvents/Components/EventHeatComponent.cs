namespace Content.Server.StationEvents.Components;

/// <summary>
///     "Danger budget" data for a game rule (a station event or an antag rule). Read by
///     <see cref="Content.Server.StationEvents.StationHeatSystem"/> to track how chaotic the round currently is, and
///     by <see cref="EventManagerSystem"/> to gate which events are valid for the schedulers to pick.
/// </summary>
/// <remarks>
///     An event with no <see cref="EventHeatComponent"/> at all is treated as the default cost (see
///     <c>events.heat_baseline</c>, 50) — i.e. an "average" event. Set an explicit <see cref="Cost"/> to mark an
///     event as weaker (loot/flavor) or stronger (overt threats).
/// </remarks>
[RegisterComponent]
public sealed partial class EventHeatComponent : Component
{
    /// <summary>
    ///     How much "heat" (chaos / danger) this rule represents, measured roughly in minutes-of-chaos. Higher = more
    ///     disruptive. Default 50 = an average event. Suggested scale (with the default ceiling of 300): ~10-20 =
    ///     loot/flavor, ~40-70 = minor/standard disruption, ~100-170 = serious midround threat (ninja, dragon, sleeper,
    ///     lone ops), ~210 = round-defining overt threat (nukies, xenoborgs, zombie outbreak).
    /// </summary>
    [DataField]
    public float Cost = 50f;

    /// <summary>
    ///     If true, <see cref="Cost"/> is contributed continuously for as long as the rule is active and not yet
    ///     <see cref="Released"/> (use for ongoing antags whose game rule persists, e.g. Nukeops, xenoborgs).
    ///     If false (default), <see cref="Cost"/> is injected once as a decaying impulse when the rule starts
    ///     (use for one-shot environmental events like gas leaks or meteor swarms).
    /// </summary>
    [DataField]
    public bool Sustained;

    /// <summary>
    ///     Set by <see cref="Content.Server.StationEvents.StationHeatSystem.ReleaseSustained"/> when a sustained
    ///     threat's heat is released to decay (e.g. some time after the sector threat alert fires). Once released the
    ///     cost no longer counts as a live sustained contribution; it was added to the decaying impulse pool instead.
    /// </summary>
    [ViewVariables]
    public bool Released;
}
