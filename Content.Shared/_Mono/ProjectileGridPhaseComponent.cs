using Robust.Shared.GameStates;

namespace Content.Shared._Mono;

/// <summary>
/// Marker component for projectiles that should phase through (ignore collisions with) entities on
/// their origin grid - i.e. the ship that fired them. Networked so the client predicts the same
/// phasing the server applies, which keeps a ship's own shells from colliding with / being slowed
/// by / re-parented to their own (often rotating) hull and shield. Ported from Triad Sector, where
/// this is what keeps ship-gun shells flying straight on the map instead of "originating from the
/// grid centre" with broken convergence.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProjectileGridPhaseComponent : Component
{
    /// <summary>
    /// The grid the projectile was spawned from. Collisions with entities on this grid are ignored.
    /// </summary>
    [ViewVariables]
    public EntityUid? SourceGrid;
}
