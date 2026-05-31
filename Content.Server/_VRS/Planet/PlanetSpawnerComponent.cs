using System.Numerics;
using Content.Shared.Parallax.Biomes.Markers;
using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Server._VRS.Planet;

/// <summary>
/// Drives ambient world-population on a persistent planet:
/// <list type="bullet">
///   <item>Adds a configured set of <see cref="BiomeMarkerLayerPrototype"/>s
///         on map init so wandering mob packs spawn naturally as players
///         explore (Poisson-disc spacing handled by the marker system).</item>
///   <item>Periodically rolls to procedurally spawn dungeons in regions
///         that are clear of player plots, shuttles and previously-spawned
///         dungeons.</item>
/// </list>
/// Place this on the planet's map entity alongside <see cref="PlanetPlotRegistryComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class PlanetSpawnerComponent : Component
{
    /// <summary>
    /// Wandering-mob marker layers added to the planet's <see cref="BiomeComponent"/> on map init.
    /// </summary>
    [DataField]
    public List<ProtoId<BiomeMarkerLayerPrototype>> WanderingMobLayers = new()
    {
        "VRSWanderingXenos",
        "VRSWanderingXenoDrones",
        "VRSWanderingXenoRunners",
        "VRSWanderingExplorersMelee",
        "VRSWanderingExplorersRanged",
        "VRSWanderingCarpsSalvage",
    };

    /// <summary>
    /// Pool of dungeon configurations the spawner can roll. Reuses the same
    /// configs salvage expeditions use.
    /// </summary>
    [DataField]
    public List<ProtoId<DungeonConfigPrototype>> DungeonConfigs = new()
    {
        "NFExperiment",
        "NFHaunted",
        "NFLavaBrig",
        "NFMineshaft",
        "NFSnowyLabs",
        "NFCaveFactory",
        "NFMedSci",
        "NFFactoryDorms",
    };

    /// <summary>
    /// How often a dungeon-spawn roll is attempted. A longer interval combined
    /// with a higher per-roll chance gives the same expected spawn cadence as a
    /// short interval + low chance, but with far fewer Update wakeups and
    /// far fewer wasted exclusion scans.
    /// </summary>
    [DataField]
    public TimeSpan RollInterval = TimeSpan.FromSeconds(45);

    /// <summary>Probability per roll of *attempting* placement (capped by exclusions).</summary>
    [DataField]
    public float SpawnChancePerRoll = 1.0f;

    /// <summary>
    /// Hard ceiling so the planet doesn't get carpet-bombed. Each spawned
    /// dungeon permanently inflates the planet grid's entity count, so this
    /// directly bounds long-session memory growth.
    /// </summary>
    [DataField]
    public int MaxDungeons = 6;

    /// <summary>
    /// Number of dungeons pre-seeded immediately when the planet is created,
    /// at random offsets from the origin in <see cref="PreseedRingMin"/>..<see cref="PreseedRingMax"/>.
    /// Guarantees dungeons exist (and have FTL beacons) before any player
    /// exploration triggers a chunk-driven roll.
    /// </summary>
    [DataField]
    public int PreseedDungeonCount = 4;

    /// <summary>Inner radius (world tiles) for the pre-seeded dungeon ring.</summary>
    [DataField]
    public float PreseedRingMin = 350f;

    /// <summary>Outer radius (world tiles) for the pre-seeded dungeon ring.</summary>
    [DataField]
    public float PreseedRingMax = 1200f;

    /// <summary>
    /// Number of mobs spawned around each dungeon at preseed time. Mobs are
    /// drawn from <see cref="RampingMobs"/> and placed in a ring around the
    /// dungeon center; this gives every dungeon a baseline garrison since
    /// <see cref="Content.Server.Procedural.DungeonSystem.GenerateDungeon"/>
    /// only places terrain/structures.
    /// </summary>
    [DataField]
    public int PreseedMobsPerDungeon = 25;

    /// <summary>Inner ring radius (tiles) for dungeon garrison spawn.</summary>
    [DataField]
    public float PreseedMobInnerRadius = 6f;

    /// <summary>Outer ring radius (tiles) for dungeon garrison spawn.</summary>
    [DataField]
    public float PreseedMobOuterRadius = 28f;

    /// <summary>Minimum world-tile distance from any player plot or shuttle.</summary>
    [DataField]
    public float MinDistFromActivity = 96f;

    /// <summary>Minimum world-tile distance between any two spawned dungeons.</summary>
    [DataField]
    public float MinDistBetweenDungeons = 600f;

    /// <summary>
    /// Radius (world tiles) checked around an existing dungeon to decide if it
    /// has been "cleared". Dungeons with no live AI mobs inside this radius are
    /// pruned from the spawn list before the cap check, allowing fresh ones to
    /// spawn elsewhere on the planet. Set roughly to the dungeon footprint plus
    /// the wandering-mob leash distance.
    /// </summary>
    [DataField]
    public float DungeonClearanceRadius = 600f;

    /// <summary>Set true once <see cref="WanderingMobLayers"/> have been registered.</summary>
    [ViewVariables]
    public bool MarkerLayersAdded;

    // ── FTL-arrival dungeon trigger ─────────────────────────────────────────

    /// <summary>
    /// Minimum interval between FTL-arrival-triggered dungeon spawns on this
    /// planet. Prevents a coordinated shuttle convoy or rapid round-trip
    /// landings from spawning multiple dungeons back-to-back.
    /// </summary>
    [DataField]
    public TimeSpan FtlSpawnCooldown = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Inner radius (world tiles) for FTL-arrival dungeon placement, measured
    /// from the arriving shuttle's centre. Keeps fresh dungeons out of the
    /// shuttle's immediate landing footprint.
    /// </summary>
    [DataField]
    public float FtlSpawnRingMin = 200f;

    /// <summary>
    /// Outer radius (world tiles) for FTL-arrival dungeon placement.
    /// </summary>
    [DataField]
    public float FtlSpawnRingMax = 400f;

    /// <summary>
    /// Server time of the next allowed FTL-arrival dungeon spawn on this planet.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextFtlSpawn;

    // ── Ramping ambient mob density ─────────────────────────────────────────

    /// <summary>
    /// Pool of mob prototypes the ramping ambient spawner draws from. By
    /// default mirrors the species used in <see cref="WanderingMobLayers"/>
    /// so the density curve looks consistent.
    /// </summary>
    [DataField]
    public List<EntProtoId> RampingMobs = new()
    {
        "MobXeno",
        "MobXenoDroneNPC",
        "MobXenoRunnerNPC",
        "MobExplorerMeleeT1",
        "MobExplorerRangedT1",
        "MobCarpSalvage",
    };

    /// <summary>Server time the planet started ramping (set on map init).</summary>
    [ViewVariables]
    public TimeSpan RampStart;

    /// <summary>Server time of the next ramping ambient mob spawn.</summary>
    [ViewVariables]
    public TimeSpan NextRampSpawn;

    /// <summary>
    /// Interval at the very start of the round between ambient ramping spawns.
    /// Decays linearly toward <see cref="RampMinIntervalSeconds"/> across
    /// <see cref="RampPeakMinutes"/>.
    /// </summary>
    [DataField]
    public float RampStartIntervalSeconds = 60f;

    /// <summary>Floor for the spawn interval after fully ramping up.</summary>
    [DataField]
    public float RampMinIntervalSeconds = 8f;

    /// <summary>
    /// Pack size at round start; the upper bound rises with time. Per-spawn
    /// pack size is rolled in [RampStartGroup, RampStartGroup + extra(time)].
    /// </summary>
    [DataField]
    public int RampStartGroup = 1;

    /// <summary>
    /// Maximum extra mobs added to the pack roll at full ramp. So a pack at
    /// peak ramp can be up to <see cref="RampStartGroup"/> + this in size.
    /// </summary>
    [DataField]
    public int RampMaxExtraGroup = 4;

    /// <summary>
    /// Real-time minutes from <see cref="RampStart"/> at which the ramp is
    /// fully scaled. Beyond this point the spawner stays at peak density.
    /// </summary>
    [DataField]
    public float RampPeakMinutes = 60f;

    /// <summary>
    /// Maximum simultaneous live ambient ramp mobs. Once reached, new spawns
    /// are skipped until older ones die or wander out of tracking.
    /// </summary>
    [DataField]
    public int RampLiveCap = 80;

    /// <summary>
    /// Maximum world-tile distance from a player at which a ramping ambient
    /// mob can be spawned. Keeps spawns relevant rather than seeding empty
    /// regions of the planet.
    /// </summary>
    [DataField]
    public float RampSpawnRadius = 96f;

    /// <summary>
    /// Minimum world-tile distance from a player for a ramping spawn so the
    /// player isn't ambushed inside their own viewport.
    /// </summary>
    [DataField]
    public float RampMinSpawnDistance = 24f;

    /// <summary>
    /// Live entities the ramping spawner has placed. Used for the live cap
    /// check; cleaned up lazily as entries are missing/dead.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> LiveRampMobs = new();

    /// <summary>Set true once initial dungeons have been pre-seeded for this planet.</summary>
    [ViewVariables]
    public bool Preseeded;

    /// <summary>Index of the next pre-seed dungeon to place; staggered across ticks.</summary>
    [ViewVariables]
    public int PreseedIndex;

    /// <summary>Server time of the next dungeon-spawn roll.</summary>
    [ViewVariables]
    public TimeSpan NextRoll;

    // ── Planet combat contract events ─────────────────────────────────────────

    /// <summary>
    /// How often to roll for a new combat contract event.
    /// </summary>
    [DataField]
    public TimeSpan ContractRollInterval = TimeSpan.FromMinutes(4);

    /// <summary>
    /// Per-roll chance to spawn a contract event when below
    /// <see cref="MaxActiveContracts"/>.
    /// </summary>
    [DataField]
    public float ContractSpawnChancePerRoll = 0.65f;

    /// <summary>
    /// Maximum simultaneous contract events on this planet.
    /// </summary>
    [DataField]
    public int MaxActiveContracts = 2;

    /// <summary>
    /// Server time of the next contract roll.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextContractRoll;

    /// <summary>
    /// How often active contracts are scanned for completion.
    /// </summary>
    [DataField]
    public TimeSpan ContractStatusCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Server time of the next active-contract completion scan.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextContractStatusCheck;

    /// <summary>
    /// Probability a contract event is a dungeon assault instead of a roaming
    /// hunt encounter.
    /// </summary>
    [DataField]
    public float DungeonContractChance = 0.45f;

    /// <summary>
    /// Contract mob spawn ring inner radius in world tiles.
    /// </summary>
    [DataField]
    public float ContractMobInnerRadius = 8f;

    /// <summary>
    /// Contract mob spawn ring outer radius in world tiles.
    /// </summary>
    [DataField]
    public float ContractMobOuterRadius = 34f;

    /// <summary>
    /// No-landing radius around a contract marker beacon.
    /// </summary>
    [DataField]
    public float ContractFtlLandingRadius = 50f;

    /// <summary>
    /// Active contract events currently running on this planet.
    /// </summary>
    [ViewVariables]
    public List<PlanetCombatContract> ActiveContracts = new();

    /// <summary>World positions of dungeons we've already placed this round.</summary>
    [ViewVariables]
    public List<Vector2> SpawnedDungeons = new();
}

public sealed class PlanetCombatContract
{
    public string Name = string.Empty;
    public string DifficultyName = string.Empty;
    public bool IsDungeonContract;
    public Vector2 Center;
    public string RewardBriefcaseProto = string.Empty;
    public EntityUid? Beacon;
    public EntityUid? Boss;
    public List<EntityUid> Members = new();
    public Vector2? DungeonCenter;
}
