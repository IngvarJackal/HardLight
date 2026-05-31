using Content.Shared.Explosion;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Radiation;

[RegisterComponent]
public sealed partial class ChainRadiationComponent : Component
{
    /// <summary>
    /// Base radiation intensity. Overrides what's specified in RadiationSourceComponent.
    /// </summary>
    [DataField]
    public float BaseIntensity = 0.5f;

    /// <summary>
    /// How much to increase our emitted radiation per unit of radiation received.
    /// </summary>
    [DataField]
    public float Coefficient = 0.01f;

    /// <summary>
    /// Explode upon reaching this amount of radiation emitted.
    /// </summary>
    [DataField]
    public float ExplosionThreshold = 2000f;

    /// <summary>
    /// VRS: explosion prototype to use when triggering. Mono originally pulled
    /// this from the entity's <c>ExplosiveComponent</c>; ExplosiveComponent has
    /// since been locked to SharedExplosionSystem access in upstream, so we
    /// configure the explosion entirely on this component instead.
    /// </summary>
    [DataField]
    public ProtoId<ExplosionPrototype> ExplosionType = "Default";

    /// <summary>
    /// TotalIntensity of explosion to make when reaching threshold.
    /// </summary>
    [DataField]
    public float TotalIntensity = 100000f; // legalize nuclear bombs

    /// <summary>
    /// IntensitySlope of explosion to make when reaching threshold.
    /// </summary>
    [DataField]
    public float IntensitySlope = 20f;

    /// <summary>
    /// MaxIntensity of explosion to make when reaching threshold.
    /// </summary>
    [DataField]
    public float MaxIntensity = 800f;

    /// <summary>
    /// When exploding due to exceeding threshold, also explode other ChainRadiation entities in this radius.
    /// If you're reading this and want this to only affect entities of a certain kind, add an EntityWhitelist.
    /// </summary>
    [DataField]
    public float ChainExplosionRadius = 4f;
}
