using Robust.Shared.GameStates;

namespace Content.Server._VRS.Planet;

/// <summary>
/// Marks a beacon as a planet contract no-landing zone center. Shuttles must
/// land outside <see cref="NoLandingRadius"/> around this beacon.
/// </summary>
[RegisterComponent]
public sealed partial class PlanetEventFTLOverrideComponent : Component
{
    [DataField]
    public float NoLandingRadius = 50f;
}
