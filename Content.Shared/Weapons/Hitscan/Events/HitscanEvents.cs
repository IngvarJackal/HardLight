using Content.Shared.Damage;

namespace Content.Shared.Weapons.Hitscan.Events;

/// <summary>
/// Raised on the firing entity (gun) when a hitscan projectile successfully deals damage to a target.
/// Ported from Triad_Sector.
/// </summary>
[ByRefEvent]
public record struct HitscanDamageDealtEvent
{
    /// <summary>
    /// The entity that was dealt hitscan damage.
    /// </summary>
    public EntityUid Target;

    /// <summary>
    /// The actual damage dealt, after modifiers.
    /// </summary>
    public DamageSpecifier DamageDealt;
}
