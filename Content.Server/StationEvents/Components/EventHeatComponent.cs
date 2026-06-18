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
    ///     disruptive. Default 50 = an average event. <see cref="Cost"/> is injected once as a decaying impulse when
    ///     the rule starts. Suggested scale (with the default ceiling of 300): ~10-20 = loot/flavor, ~40-70 =
    ///     minor/standard disruption, ~90-130 = serious midround threat or mob-horde infestation (ninja, dragon,
    ///     sleeper, vent hordes).
    /// </summary>
    [DataField]
    public float Cost = 50f;
}
