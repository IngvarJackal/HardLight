using Content.Shared.Parallax.Biomes;
using Robust.Shared.Prototypes;

namespace Content.Server._VRS.Planet;

/// <summary>
/// Game rule that spawns a single persistent biome planet at round start with
/// a Landgrab plot registry, ambient-mob marker layers and procedural dungeon
/// rolling already attached. The planet is reachable via the shuttle console
/// because the rule also drops an <c>FTLPoint</c> beacon at its origin.
/// </summary>
[RegisterComponent]
public sealed partial class VRSPersistentPlanetRuleComponent : Component
{
    /// <summary>
    /// Biome template used to procedurally generate the planet's tiles.
    /// Defaults to <c>Continental</c>: combines grass / snow / lava / caves so a
    /// single planet covers all four flavours and exploration always finds variety.
    /// </summary>
    [DataField]
    public ProtoId<BiomeTemplatePrototype> BiomeTemplate = "Continental";

    /// <summary>
    /// Optional fixed seed for deterministic biome generation. When null a
    /// random seed is rolled at rule start (different planet every round).
    /// </summary>
    [DataField]
    public int? Seed;

    /// <summary>
    /// Display-name pool. One is picked at random and shown on the shuttle
    /// console, IFF, and FTL beacon. Add to taste in the prototype YAML.
    /// </summary>
    [DataField]
    public List<string> NamePool = new()
    {
        "Aetheria",
        "New Eden",
        "Frontier-7",
        "Hadley",
        "Tartarus",
        "Vesper",
        "Yggdrasil",
        "Cygnus Reach",
        "Pyrrhus",
        "Halcyon",
    };

    /// <summary>
    /// Cost in spesos to purchase a 32x32 plot grid on this planet. Mirrored to
    /// the spawned <c>PlanetPlotRegistryComponent</c>.
    /// </summary>
    [DataField]
    public int PurchaseCost = 5000;

    /// <summary>
    /// IFF / minimap colour for the planet entity.
    /// </summary>
    [DataField]
    public Color IffColor = new(0.40f, 0.85f, 0.55f);

    /// <summary>
    /// Set after the planet has been spawned so the rule is idempotent.
    /// </summary>
    [ViewVariables]
    public EntityUid? SpawnedPlanet;
}
