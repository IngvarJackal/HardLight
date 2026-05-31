namespace Content.Shared._HL.Weapons.Components;

/// <summary>
/// Throws the shooter backwards after this gun fires.
/// </summary>
[RegisterComponent]
public sealed partial class GunUserThrowbackComponent : Component
{
    /// <summary>
    /// Base recoil vulnerability multiplier applied after shot payload and mass scaling.
    /// </summary>
    [DataField("strength")]
    public float Strength = 10f;

    /// <summary>
    /// Per-projectile contribution to recoil strength.
    /// </summary>
    [DataField("projectileMultiplier")]
    public float ProjectileMultiplier = 0.5f;

    /// <summary>
    /// Per-point-of-damage contribution to recoil strength.
    /// </summary>
    [DataField("damageMultiplier")]
    public float DamageMultiplier = 0.5f;

    /// <summary>
    /// Additional multiplier applied for each extra projectile in the same shot.
    /// </summary>
    [DataField("volleyMultiplier")]
    public float VolleyMultiplier = 0.5f;

    /// <summary>
    /// Additional multiplier applied when the fired gun is wielded.
    /// This gives larger two-handed weapons a noticeably stronger kick.
    /// </summary>
    [DataField("wieldedMultiplier")]
    public float WieldedMultiplier = 1.35f;

    /// <summary>
    /// Final size-trait multiplier applied after payload, mass, and wield scaling.
    /// </summary>
    [DataField("impulseMultiplier")]
    public float ImpulseMultiplier = 1f;

    /// <summary>
    /// Shooter mass that maps to a 1x recoil multiplier.
    /// </summary>
    [DataField("referenceMass")]
    public float ReferenceMass = 1f;

    /// <summary>
    /// Lower clamp for the mass-derived recoil multiplier.
    /// </summary>
    [DataField("minMassFactor")]
    public float MinMassFactor = 0.1f;

    /// <summary>
    /// Upper clamp for the mass-derived recoil multiplier.
    /// </summary>
    [DataField("maxMassFactor")]
    public float MaxMassFactor = 2f;

    /// <summary>
    /// How long to stun the shooter if a wielded-gun recoil throw slams them into a hard obstacle.
    /// </summary>
    [DataField("collisionStunTime")]
    public float CollisionStunTime = 1f;

    /// <summary>
    /// Whether throwing should compensate for floor friction.
    /// </summary>
    [DataField("compensateFriction")]
    public bool CompensateFriction = true;
}