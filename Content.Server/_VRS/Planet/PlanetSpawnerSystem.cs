using System.Linq;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Parallax;
using Content.Server.Procedural;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared._VRS.Planet;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VRS.Planet;

/// <summary>
/// Drives the <see cref="PlanetSpawnerComponent"/>: registers ambient mob
/// marker layers on map init and periodically rolls dungeon placements in
/// regions clear of player activity.
/// </summary>
public sealed class PlanetSpawnerSystem : EntitySystem
{
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly DungeonSystem _dungeon = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>
    /// Beacon prototype spawned at each dungeon center so the shuttle console
    /// surfaces them as selectable FTL destinations alongside the planet itself.
    /// </summary>
    private const string DungeonBeaconProto = "FTLPoint";

    private readonly struct ContractTemplate
    {
        public readonly string DifficultyName;
        public readonly string GruntProto;
        public readonly string BossProto;
        public readonly string RewardBriefcaseProto;
        public readonly int MinGrunts;
        public readonly int MaxGrunts;
        public readonly float Weight; // Added weight for extensibility

        public ContractTemplate(
            string difficultyName,
            string gruntProto,
            string bossProto,
            string rewardBriefcaseProto,
            int minGrunts,
            int maxGrunts,
            float weight) // Added weight parameter
        {
            DifficultyName = difficultyName;
            GruntProto = gruntProto;
            BossProto = bossProto;
            RewardBriefcaseProto = rewardBriefcaseProto;
            MinGrunts = minGrunts;
            MaxGrunts = maxGrunts;
            Weight = weight; // Assign weight
        }
    }

    private static readonly ContractTemplate[] ContractTemplates =
    {
        new("Easy", "MobCarpSalvage", "MobCarpMagic", "BriefcaseBrownFilledContractEasy", 6, 9, 1f),
        new("Moderate", "MobExplorerMeleeT2", "MobExplorerBoss", "BriefcaseBrownFilledContractModerate", 8, 12, 1.5f),
        new("Hard", "MobXenoDroneNPC", "MobXenoQueenNPC", "BriefcaseBrownFilledContractHard", 10, 15, 2f),
    };

    private ISawmill _sawmill = default!;

    /// <summary>
    /// Biome chunks are 8 tiles. We sample candidate dungeon centers on a
    /// coarser grid so adjacent rolls don't keep proposing micro-overlapping
    /// positions.
    /// </summary>
    private const int CandidateStride = 32;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("planet.spawner");
        SubscribeLocalEvent<PlanetSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
    }

    /// <summary>
    /// When a shuttle finishes FTL onto a planet map, squash any HTN-driven
    /// mobs whose world position is covered by the shuttle's footprint. Mobs
    /// outside the shuttle's AABB are left alone so the surrounding garrison
    /// remains a threat.
    /// </summary>
    private void OnFtlCompleted(ref FTLCompletedEvent args)
    {
        // Only act on planet maps so we don't sweep mobs in stations / expeditions.
        if (!TryComp<PlanetSpawnerComponent>(args.MapUid, out var spawner))
            return;

        if (!TryComp<TransformComponent>(args.Entity, out var shuttleXform))
            return;

        var mapId = shuttleXform.MapID;
        if (mapId == MapId.Nullspace)
            return;

        var aabb = _lookup.GetWorldAABB(args.Entity, shuttleXform);
        // Inflate slightly so mobs partially under the hull are also caught.
        aabb = aabb.Enlarged(0.5f);

        var hits = new HashSet<Entity<HTNComponent>>();
        _lookup.GetEntitiesIntersecting(mapId, aabb, hits);

        foreach (var mob in hits)
        {
            // Don't delete anything parented to the shuttle (e.g. its own crew NPCs).
            var xform = Transform(mob.Owner);
            if (xform.GridUid == args.Entity)
                continue;
            QueueDel(mob.Owner);
        }

        // If a player shuttle just arrived, roll a single dungeon nearby so
        // there is fresh content close to the landing site. Honours the planet
        // cooldown, the global dungeon cap, and the existing exclusion checks
        // (other shuttles, plots, neighbouring dungeons), so multi-shuttle
        // sessions don't pile dungeons on top of each other.
        TrySpawnFtlArrivalDungeon((args.MapUid, spawner), args.Entity, mapId);
    }

    /// <summary>
    /// Spawns one dungeon in a random ring around an arriving player shuttle,
    /// subject to the planet's FTL cooldown, dungeon cap, and the standard
    /// activity / dungeon spacing exclusions. Returns silently on any failure
    /// (including "no clear candidate found") — the periodic Update roll will
    /// continue to attempt placements as biome chunks load.
    /// </summary>
    private void TrySpawnFtlArrivalDungeon(
        Entity<PlanetSpawnerComponent> ent,
        EntityUid arrivingEnt,
        MapId mapId)
    {
        // Only player shuttles trigger the spawn — ignore mobs / debris / contract
        // beacon FTLs / etc. The ShuttleComponent + non-biome filter mirrors the
        // existing SnapshotExclusions logic so we don't fire on the planet itself.
        if (!HasComp<ShuttleComponent>(arrivingEnt) || HasComp<BiomeComponent>(arrivingEnt))
            return;

        var now = _timing.CurTime;
        if (now < ent.Comp.NextFtlSpawn)
            return;

        if (ent.Comp.SpawnedDungeons.Count >= ent.Comp.MaxDungeons)
            return;

        if (!TryComp<MapGridComponent>(ent.Owner, out var grid))
            return;

        if (ent.Comp.DungeonConfigs.Count == 0)
            return;

        var arrivalWorld = _transform.GetWorldPosition(arrivingEnt);

        var (plotPositions, shuttlePositions) = SnapshotExclusions(mapId);
        // The arriving shuttle is now in the snapshot; that's intentional — the
        // ring offset already keeps the candidate away from it, and downstream
        // exclusion uses MinDistFromActivity which we want to honour for every
        // shuttle on the planet (multi-shuttle correctness).

        const int maxAttempts = 16;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var angle = _random.NextFloat() * MathF.Tau;
            var radius = _random.NextFloat(ent.Comp.FtlSpawnRingMin, ent.Comp.FtlSpawnRingMax);
            var candidate = arrivalWorld + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);

            if (!IsCandidatePositionClear(candidate, ent.Comp, plotPositions, shuttlePositions))
                continue;

            var snappedTile = new Vector2i(
                (int)MathF.Round(candidate.X / CandidateStride) * CandidateStride,
                (int)MathF.Round(candidate.Y / CandidateStride) * CandidateStride);

            var configId = _random.Pick(ent.Comp.DungeonConfigs);
            if (!_proto.TryIndex(configId, out var config))
            {
                _sawmill.Warning($"PlanetSpawner missing dungeon config '{configId}' (ftl arrival).");
                continue;
            }

            var seed = HashCode.Combine(now.Ticks, snappedTile.X, snappedTile.Y);
            _dungeon.GenerateDungeon(config, configId.Id, ent.Owner, grid, snappedTile, seed);
            var snappedWorld = new Vector2(snappedTile.X, snappedTile.Y);
            ent.Comp.SpawnedDungeons.Add(snappedWorld);

            var beacon = Spawn(DungeonBeaconProto, new EntityCoordinates(ent.Owner, snappedWorld));
            _meta.SetEntityName(beacon, configId.Id);

            var registry = EnsureComp<PlanetDungeonRegistryComponent>(ent.Owner);
            registry.Dungeons.Add(new DungeonMarker(snappedWorld, configId.Id));
            Dirty(ent.Owner, registry);

            ent.Comp.NextFtlSpawn = now + ent.Comp.FtlSpawnCooldown;
            _sawmill.Info($"Spawned FTL-arrival dungeon '{configId}' at ({snappedTile.X}, {snappedTile.Y}) on {ToPrettyString(ent.Owner)} for arriving shuttle {ToPrettyString(arrivingEnt)}.");
            return;
        }
    }

    private void OnMapInit(Entity<PlanetSpawnerComponent> ent, ref MapInitEvent args)
    {
        var contractStatusInterval = ent.Comp.ContractStatusCheckInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : ent.Comp.ContractStatusCheckInterval;

        EnsureMarkerLayers(ent);
        // Dungeons are now spawned lazily on FTL arrival (see OnFtlCompleted) and
        // by the periodic Update roll once players begin loading biome chunks.
        // No preseed work is performed at round start — that previously generated
        // 4 dungeons + ~100 HTN garrison mobs in a single tick, causing the
        // observed round-start GameState burst and "MainLoop: Cannot keep up!".
        ent.Comp.NextRoll = _timing.CurTime + ent.Comp.RollInterval;
        ent.Comp.NextContractRoll = _timing.CurTime + ent.Comp.ContractRollInterval;
        ent.Comp.NextContractStatusCheck = _timing.CurTime + contractStatusInterval;
        ent.Comp.RampStart = _timing.CurTime;
        ent.Comp.NextRampSpawn = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.RampStartIntervalSeconds);
    }

    /// <summary>
    /// Returns true if a mob spawned at the given tile would clip into a wall,
    /// rock, or other impassable static entity — either an already-anchored
    /// entity on the grid, or one the biome generator predicts will spawn there
    /// once the chunk loads.
    /// </summary>
    private bool IsTileBlockedForMob(EntityUid gridUid, MapGridComponent grid, BiomeComponent? biome, Vector2i tile)
    {
        // Check anchored entities (dungeon walls, already-spawned biome rocks).
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var anchoredUid))
        {
            if (!TryComp<PhysicsComponent>(anchoredUid, out var body))
                continue;
            if (body.BodyType != BodyType.Static || !body.Hard)
                continue;
            if ((body.CollisionLayer & (int)CollisionGroup.Impassable) != 0)
                return true;
        }

        // Check what the biome generator would place here when the chunk loads.
        if (biome != null && _biome.TryGetEntity(tile, biome, grid, out _))
            return true;

        return false;
    }

    private void EnsureMarkerLayers(Entity<PlanetSpawnerComponent> ent)
    {
        if (ent.Comp.MarkerLayersAdded)
            return;
        if (!TryComp<BiomeComponent>(ent.Owner, out var biome))
            return;

        foreach (var layerId in ent.Comp.WanderingMobLayers)
        {
            if (!_proto.HasIndex(layerId))
            {
                _sawmill.Warning($"PlanetSpawner on {ToPrettyString(ent.Owner)} references missing marker layer '{layerId}'.");
                continue;
            }
            // BiomeComponent.MarkerLayers is access-restricted; rely on the
            // MarkerLayersAdded flag (set below) as the dedupe guard.
            _biome.AddMarkerLayer(ent.Owner, biome, layerId.Id);
        }
        ent.Comp.MarkerLayersAdded = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<PlanetSpawnerComponent>();
        while (query.MoveNext(out var uid, out var spawner))
        {
            // Re-attempt marker layer registration if biome wasn't ready at MapInit
            // (e.g. when added at runtime via admin tooling).
            if (!spawner.MarkerLayersAdded)
                EnsureMarkerLayers((uid, spawner));

            // Time-scaled ambient mob spawns. Independent of the dungeon roll
            // cadence — fires on its own ramping cooldown.
            if (spawner.RampStart == TimeSpan.Zero)
                spawner.RampStart = now;
            if (now >= spawner.NextRampSpawn)
            {
                TrySpawnRampMob((uid, spawner), now);
                spawner.NextRampSpawn = now + TimeSpan.FromSeconds(GetRampInterval(spawner, now));
            }

            var contractStatusInterval = spawner.ContractStatusCheckInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : spawner.ContractStatusCheckInterval;

            if (spawner.NextContractStatusCheck == TimeSpan.Zero)
                spawner.NextContractStatusCheck = now + contractStatusInterval;
            if (now >= spawner.NextContractStatusCheck)
            {
                UpdateContracts((uid, spawner));
                spawner.NextContractStatusCheck = now + contractStatusInterval;
            }

            if (now >= spawner.NextContractRoll)
            {
                spawner.NextContractRoll = now + spawner.ContractRollInterval;

                if (spawner.ActiveContracts.Count < spawner.MaxActiveContracts &&
                    _random.NextFloat() <= spawner.ContractSpawnChancePerRoll)
                {
                    TrySpawnCombatContract((uid, spawner));
                }
            }

            if (now < spawner.NextRoll)
                continue;
            spawner.NextRoll = now + spawner.RollInterval;

            // Drop dungeons that no longer have any live AI mobs in their
            // clearance radius. Once nothing is wandering or shooting at the
            // structure it stops costing meaningful per-tick CPU, so we let
            // the spawner replace it elsewhere on the planet.
            PruneClearedDungeons((uid, spawner));

            // Cheap guards before any candidate work:
            if (spawner.SpawnedDungeons.Count >= spawner.MaxDungeons)
                continue; // hard cap reached, never spend cycles searching
            if (_random.NextFloat() > spawner.SpawnChancePerRoll)
                continue;

            TryRollDungeon((uid, spawner));
        }
    }

    private void UpdateContracts(Entity<PlanetSpawnerComponent> ent)
    {
        if (ent.Comp.ActiveContracts.Count == 0)
            return;

        for (var i = ent.Comp.ActiveContracts.Count - 1; i >= 0; i--)
        {
            var contract = ent.Comp.ActiveContracts[i];

            for (var m = contract.Members.Count - 1; m >= 0; m--)
            {
                if (!IsAliveMob(contract.Members[m]))
                    contract.Members.RemoveAt(m);
            }

            if (contract.Members.Count > 0)
                continue;

            var dropCoords = contract.Boss != null && Exists(contract.Boss.Value)
                ? Transform(contract.Boss.Value).Coordinates
                : new EntityCoordinates(ent.Owner, contract.Center);

            if (!string.IsNullOrWhiteSpace(contract.RewardBriefcaseProto))
                Spawn(contract.RewardBriefcaseProto, dropCoords);

            if (contract.Beacon != null && Exists(contract.Beacon.Value))
                QueueDel(contract.Beacon.Value);

            if (contract.DungeonCenter is { } dungeonCenter)
                RemoveDungeonTracking((ent.Owner, ent.Comp), dungeonCenter);

            _chat.DispatchGlobalAnnouncement(
                Loc.GetString(
                    "vrs-planet-contract-complete-announcement",
                    ("name", contract.Name),
                    ("difficulty", contract.DifficultyName)));

            ent.Comp.ActiveContracts.RemoveAt(i);
        }
    }

    private void TrySpawnCombatContract(Entity<PlanetSpawnerComponent> ent)
    {
        if (!TryComp<BiomeComponent>(ent.Owner, out var biome) ||
            !TryComp<MapGridComponent>(ent.Owner, out var grid) ||
            !TryComp<TransformComponent>(ent.Owner, out var xform))
        {
            return;
        }

        if (!TryFindContractCenter((ent.Owner, ent.Comp), biome, xform.MapID, out var centerTile))
            return;

        var template = PickContractTemplate();
        var isDungeon = _random.NextFloat() <= ent.Comp.DungeonContractChance;
        var center = new Vector2(centerTile.X, centerTile.Y);
        var contractType = isDungeon ? "Dungeon" : "Hunt";
        var contractName = $"{template.DifficultyName} {contractType} Contract";

        Vector2? dungeonCenter = null;
        if (isDungeon && ent.Comp.DungeonConfigs.Count > 0)
        {
            var configId = _random.Pick(ent.Comp.DungeonConfigs);
            if (_proto.TryIndex(configId, out var config))
            {
                var seed = HashCode.Combine(_timing.CurTime.Ticks, centerTile.X, centerTile.Y, contractName);
                _dungeon.GenerateDungeon(config, configId.Id, ent.Owner, grid, centerTile, seed);
                ent.Comp.SpawnedDungeons.Add(center);

                var registry = EnsureComp<PlanetDungeonRegistryComponent>(ent.Owner);
                registry.Dungeons.Add(new DungeonMarker(center, contractName));
                Dirty(ent.Owner, registry);

                dungeonCenter = center;
            }
            else
            {
                _sawmill.Warning($"PlanetSpawner missing dungeon config '{configId}' (contract). Falling back to hunt event.");
                isDungeon = false;
                contractType = "Hunt";
                contractName = $"{template.DifficultyName} {contractType} Contract";
            }
        }

        var beaconCoords = new EntityCoordinates(ent.Owner, center);
        var beacon = Spawn(DungeonBeaconProto, beaconCoords);
        _meta.SetEntityName(beacon, contractName);
        var overrideComp = EnsureComp<PlanetEventFTLOverrideComponent>(beacon);
        overrideComp.NoLandingRadius = ent.Comp.ContractFtlLandingRadius;

        var members = new List<EntityUid>();
        var gruntCount = _random.Next(template.MinGrunts, template.MaxGrunts + 1);
        SpawnContractMobPack((ent.Owner, ent.Comp), center, template.GruntProto, gruntCount, members, grid, biome); // Pass grid and biome

        var bossUid = Spawn(template.BossProto, new EntityCoordinates(ent.Owner, center));
        members.Add(bossUid);

        ent.Comp.ActiveContracts.Add(new PlanetCombatContract
        {
            Name = contractName,
            DifficultyName = template.DifficultyName,
            IsDungeonContract = isDungeon,
            Center = center,
            RewardBriefcaseProto = template.RewardBriefcaseProto,
            Beacon = beacon,
            Boss = bossUid,
            Members = members,
            DungeonCenter = dungeonCenter,
        });

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(
                "vrs-planet-contract-start-announcement",
                ("name", contractName),
                ("difficulty", template.DifficultyName),
                ("type", contractType),
                ("landing", (int)MathF.Round(ent.Comp.ContractFtlLandingRadius))));
    }

    private bool TryFindContractCenter(
        Entity<PlanetSpawnerComponent> ent,
        BiomeComponent biome,
        MapId mapId,
        out Vector2i centerTile)
    {
        centerTile = default;

        var loadedChunks = biome.LoadedChunks;
        if (loadedChunks.Count == 0)
            return false;

        var (plotPositions, shuttlePositions) = SnapshotExclusions(mapId);
        var sample = SampleLoadedChunks(loadedChunks, 16);
        var activeContractDistSq = ent.Comp.MinDistFromActivity * ent.Comp.MinDistFromActivity;

        foreach (var chunk in sample)
        {
            var worldPos = new Vector2(chunk.X + 4f, chunk.Y + 4f);
            if (!IsCandidatePositionClear(worldPos, ent.Comp, plotPositions, shuttlePositions))
                continue;

            var tooCloseToContract = false;
            foreach (var contract in ent.Comp.ActiveContracts)
            {
                if ((worldPos - contract.Center).LengthSquared() < activeContractDistSq)
                {
                    tooCloseToContract = true;
                    break;
                }
            }

            if (tooCloseToContract)
                continue;

            centerTile = new Vector2i(
                (int)MathF.Round(worldPos.X / CandidateStride) * CandidateStride,
                (int)MathF.Round(worldPos.Y / CandidateStride) * CandidateStride);
            return true;
        }

        return false;
    }

    private ContractTemplate PickContractTemplate()
    {
        var totalWeight = 0f;
        foreach (var t in ContractTemplates)
            totalWeight += t.Weight;

        var roll = _random.NextFloat() * totalWeight;
        var cumulative = 0f;
        foreach (var t in ContractTemplates)
        {
            cumulative += t.Weight;
            if (roll < cumulative)
                return t;
        }
        return ContractTemplates[ContractTemplates.Length - 1];
    }

    private void SpawnContractMobPack(
        Entity<PlanetSpawnerComponent> ent,
        Vector2 center,
        string mobProto,
        int count,
        List<EntityUid> members,
        MapGridComponent grid,
        BiomeComponent biome)
    {
        const int maxAttemptsPerSlot = 8;

        for (var i = 0; i < count; i++)
        {
            Timer.Spawn(i * 100, () => // Stagger spawns by 100ms each
            {
                for (var attempt = 0; attempt < maxAttemptsPerSlot; attempt++)
                {
                    var angle = _random.NextFloat() * MathF.Tau;
                    var dist = _random.NextFloat(ent.Comp.ContractMobInnerRadius, ent.Comp.ContractMobOuterRadius);
                    var offset = new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                    var localPos = center + offset;
                    var tileIndices = new Vector2i((int)MathF.Floor(localPos.X), (int)MathF.Floor(localPos.Y));

                    if (IsTileBlockedForMob(ent.Owner, grid, biome, tileIndices))
                        continue;

                    var spawned = Spawn(mobProto, new EntityCoordinates(ent.Owner, localPos));
                    members.Add(spawned);
                    break;
                }
            });
        }
    }

    private bool IsAliveMob(EntityUid uid)
    {
        if (!Exists(uid))
            return false;
        if (!TryComp<MobStateComponent>(uid, out var state))
            return false;
        return _mobState.IsAlive(uid, state);
    }

    private void RemoveDungeonTracking(Entity<PlanetSpawnerComponent> ent, Vector2 center)
    {
        const float centerToleranceSq = 0.01f;

        for (var i = ent.Comp.SpawnedDungeons.Count - 1; i >= 0; i--)
        {
            if (Vector2.DistanceSquared(ent.Comp.SpawnedDungeons[i], center) <= centerToleranceSq)
                ent.Comp.SpawnedDungeons.RemoveAt(i);
        }

        if (!TryComp<PlanetDungeonRegistryComponent>(ent.Owner, out var registry))
            return;

        var dirty = false;
        for (var i = registry.Dungeons.Count - 1; i >= 0; i--)
        {
            if (Vector2.DistanceSquared(registry.Dungeons[i].Position, center) > centerToleranceSq)
                continue;
            registry.Dungeons.RemoveAt(i);
            dirty = true;
        }

        if (dirty)
            Dirty(ent.Owner, registry);
    }

    /// <summary>
    /// Picks a candidate position from the planet's loaded biome chunks,
    /// validates exclusions, and (if all checks pass) generates one dungeon.
    /// At most one dungeon per call to keep the dungeon job queue from spiking.
    /// </summary>
    private void TryRollDungeon(Entity<PlanetSpawnerComponent> ent)
    {
        if (!TryComp<BiomeComponent>(ent.Owner, out var biome))
            return;
        if (!TryComp<MapGridComponent>(ent.Owner, out var grid))
            return; // planet must be a biome-on-grid (matches salvage pattern)
        if (!TryComp<TransformComponent>(ent.Owner, out var xform))
            return;

        var loadedChunks = biome.LoadedChunks;
        if (loadedChunks.Count == 0)
            return; // nobody is exploring; nothing to gate against and nothing to populate

        // Snapshot exclusion sources ONCE per roll. The original implementation
        // re-ran two EntityQueryEnumerators inside the per-candidate loop,
        // duplicating O(plots + shuttles) component lookups for every one of the
        // 12 sampled chunks. Snapshotting collapses that to a single pass.
        var (plotPositions, shuttlePositions) = SnapshotExclusions(xform.MapID);

        // Pull a small sample of loaded chunks instead of scanning all, so a
        // huge planet doesn't make every roll expensive.
        var sample = SampleLoadedChunks(loadedChunks, 12);

        foreach (var chunk in sample)
        {
            // Center of the chunk in world tiles. Biome chunk size is 8.
            var worldPos = new Vector2(chunk.X + 4f, chunk.Y + 4f);

            if (!IsCandidatePositionClear(worldPos, ent.Comp, plotPositions, shuttlePositions))
                continue;

            // Snap to candidate stride so we don't keep proposing
            // sub-tile-different positions on the same chunk roll-after-roll.
            var snappedTile = new Vector2i(
                (int)MathF.Round(worldPos.X / CandidateStride) * CandidateStride,
                (int)MathF.Round(worldPos.Y / CandidateStride) * CandidateStride);

            var configId = _random.Pick(ent.Comp.DungeonConfigs);
            if (!_proto.TryIndex(configId, out var config))
            {
                _sawmill.Warning($"PlanetSpawner missing dungeon config '{configId}'.");
                continue;
            }

            // Seed combines time + position so re-rolls don't produce identical layouts.
            var seed = HashCode.Combine(_timing.CurTime.Ticks, snappedTile.X, snappedTile.Y);

            _dungeon.GenerateDungeon(config, configId.Id, ent.Owner, grid, snappedTile, seed);
            ent.Comp.SpawnedDungeons.Add(new Vector2(snappedTile.X, snappedTile.Y));

            // Spawn an FTL beacon at the dungeon center so it appears in the
            // shuttle console destination list (the GetBeacons enumerator on
            // SharedShuttleSystem is what populates the MAP tab sidebar).
            var beaconCoords = new EntityCoordinates(ent.Owner, new Vector2(snappedTile.X, snappedTile.Y));
            var beacon = Spawn(DungeonBeaconProto, beaconCoords);
            _meta.SetEntityName(beacon, configId.Id);

            // Mirror to the networked registry so clients (shuttle console map
            // preview) can draw dungeon markers even when the biome chunks
            // containing the dungeon haven't been loaded by that client.
            var registry = EnsureComp<PlanetDungeonRegistryComponent>(ent.Owner);
            registry.Dungeons.Add(new DungeonMarker(new Vector2(snappedTile.X, snappedTile.Y), configId.Id));
            Dirty(ent.Owner, registry);

            _sawmill.Info($"Spawned planet dungeon '{configId}' at ({snappedTile.X}, {snappedTile.Y}) on {ToPrettyString(ent.Owner)}.");
            return; // one per roll
        }
    }

    /// <summary>
    /// Single-pass collection of exclusion-relevant world positions on a given map,
    /// reused across all candidate chunk checks within one dungeon-spawn roll.
    /// </summary>
    private (List<Vector2> Plots, List<Vector2> Shuttles) SnapshotExclusions(MapId mapId)
    {
        var plots = new List<Vector2>();
        var plotQ = EntityQueryEnumerator<LandgrabPlotComponent, TransformComponent>();
        while (plotQ.MoveNext(out var plotUid, out _, out var plotXform))
        {
            if (plotXform.MapID != mapId)
                continue;
            plots.Add(_transform.GetWorldPosition(plotUid));
        }

        var shuttles = new List<Vector2>();
        var shuttleQ = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (shuttleQ.MoveNext(out var shuttleUid, out _, out var shuttleXform))
        {
            if (shuttleXform.MapID != mapId)
                continue;
            // Skip the planet's own grid (it's not a "ship currently on it").
            if (HasComp<BiomeComponent>(shuttleUid))
                continue;
            shuttles.Add(_transform.GetWorldPosition(shuttleUid));
        }

        return (plots, shuttles);
    }

    /// <summary>
    /// Removes dungeons whose clearance radius contains no live AI mobs
    /// (HTN-driven, alive) AND no player shuttles. Updates both the spawner's
    /// working list and the networked <see cref="PlanetDungeonRegistryComponent"/>
    /// so client preview markers also disappear once the area is "safe".
    /// The shuttle check is critical for multi-shuttle play: a shuttle parked
    /// at a cleared dungeon must keep that site reserved (excluded from new
    /// dungeon placement) until it leaves, otherwise a new dungeon could spawn
    /// directly on top of an occupied site.
    /// </summary>
    private void PruneClearedDungeons(Entity<PlanetSpawnerComponent> ent)
    {
        if (ent.Comp.SpawnedDungeons.Count == 0)
            return;
        if (!TryComp<TransformComponent>(ent.Owner, out var xform))
            return;

        var mapId = xform.MapID;
        var radius = ent.Comp.DungeonClearanceRadius;
        TryComp<PlanetDungeonRegistryComponent>(ent.Owner, out var registry);
        var registryDirty = false;

        // Snapshot active shuttles on this map once for the whole prune pass.
        var (_, shuttlePositions) = SnapshotExclusions(mapId);
        var clearanceSq = radius * radius;

        // Iterate in reverse so removals don't shift unscanned indices.
        for (var i = ent.Comp.SpawnedDungeons.Count - 1; i >= 0; i--)
        {
            var pos = ent.Comp.SpawnedDungeons[i];

            if (HasLiveAiMobs(new MapCoordinates(pos, mapId), radius))
                continue;

            // Don't release a dungeon's reserved spacing while a player shuttle
            // is parked nearby — even if cleared of mobs, the site is "in use".
            var shuttleNearby = false;
            foreach (var shuttlePos in shuttlePositions)
            {
                if ((pos - shuttlePos).LengthSquared() <= clearanceSq)
                {
                    shuttleNearby = true;
                    break;
                }
            }
            if (shuttleNearby)
                continue;

            ent.Comp.SpawnedDungeons.RemoveAt(i);

            if (registry != null)
            {
                // Remove the matching marker (Position-keyed; positions are
                // unique because dungeon spacing is enforced).
                for (var r = registry.Dungeons.Count - 1; r >= 0; r--)
                {
                    if (registry.Dungeons[r].Position == pos)
                    {
                        registry.Dungeons.RemoveAt(r);
                        registryDirty = true;
                        break;
                    }
                }
            }

            _sawmill.Info($"Pruned cleared dungeon at ({pos.X}, {pos.Y}) on {ToPrettyString(ent.Owner)}.");
        }

        if (registryDirty && registry != null)
            Dirty(ent.Owner, registry);
    }

    /// <summary>
    /// Returns true if at least one HTN-driven mob is alive within
    /// <paramref name="radius"/> of <paramref name="center"/>.
    /// </summary>
    private bool HasLiveAiMobs(MapCoordinates center, float radius)
    {
        foreach (var mob in _lookup.GetEntitiesInRange<HTNComponent>(center, radius))
        {
            if (!TryComp<MobStateComponent>(mob, out var state))
                continue;
            if (_mobState.IsAlive(mob, state))
                return true;
        }
        return false;
    }

    private List<Vector2i> SampleLoadedChunks(HashSet<Vector2i> chunks, int max)
    {
        if (chunks.Count <= max)
            return chunks.ToList();

        var pool = chunks.ToArray();
        var result = new List<Vector2i>(max);
        for (var i = 0; i < max; i++)
            result.Add(pool[_random.Next(pool.Length)]);
        return result;
    }

    // ── Ramping ambient mob spawner ──────────────────────────────────────────────

    /// <summary>
    /// Returns the current ramp progress in [0, 1] based on elapsed time since
    /// <see cref="PlanetSpawnerComponent.RampStart"/>.
    /// </summary>
    private static float GetRampProgress(PlanetSpawnerComponent spawner, TimeSpan now)
    {
        var peak = MathF.Max(1f, spawner.RampPeakMinutes * 60f);
        var elapsed = (float)(now - spawner.RampStart).TotalSeconds;
        return Math.Clamp(elapsed / peak, 0f, 1f);
    }

    /// <summary>
    /// Linearly interpolates the spawn interval from
    /// <see cref="PlanetSpawnerComponent.RampStartIntervalSeconds"/> down to
    /// <see cref="PlanetSpawnerComponent.RampMinIntervalSeconds"/> as the ramp
    /// progresses.
    /// </summary>
    private static float GetRampInterval(PlanetSpawnerComponent spawner, TimeSpan now)
    {
        var t = GetRampProgress(spawner, now);
        return MathHelper.Lerp(spawner.RampStartIntervalSeconds, spawner.RampMinIntervalSeconds, t);
    }

    /// <summary>
    /// Picks a random player on the planet, picks a random offset around them
    /// inside <see cref="PlanetSpawnerComponent.RampSpawnRadius"/>, and spawns
    /// a small pack of mobs there. The pack size grows linearly with ramp
    /// progress. Skipped silently if there are no players, no live cap headroom,
    /// or no valid candidate.
    /// </summary>
    private void TrySpawnRampMob(Entity<PlanetSpawnerComponent> ent, TimeSpan now)
    {
        // Trim dead/missing entries first so the cap isn't permanently saturated.
        for (var i = ent.Comp.LiveRampMobs.Count - 1; i >= 0; i--)
        {
            var mob = ent.Comp.LiveRampMobs[i];
            if (!Exists(mob))
            {
                ent.Comp.LiveRampMobs.RemoveAt(i);
                continue;
            }
            if (TryComp<MobStateComponent>(mob, out var state) && !_mobState.IsAlive(mob, state))
                ent.Comp.LiveRampMobs.RemoveAt(i);
        }

        if (ent.Comp.LiveRampMobs.Count >= ent.Comp.RampLiveCap)
            return;
        if (ent.Comp.RampingMobs.Count == 0)
            return;
        if (!TryComp<TransformComponent>(ent.Owner, out var planetXform))
            return;

        // Find players currently on this planet map.
        var planetMapId = planetXform.MapID;
        Vector2? anchor = null;
        var anchorCount = 0;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } playerEnt)
                continue;
            if (!TryComp<TransformComponent>(playerEnt, out var pxform) || pxform.MapID != planetMapId)
                continue;

            anchorCount++;
            // Reservoir-style pick so any player on the planet has equal chance.
            if (_random.Next(anchorCount) == 0)
                anchor = _transform.GetWorldPosition(playerEnt);
        }

        if (anchor is not { } anchorPos)
            return;

        // Random point in the ring [RampMinSpawnDistance, RampSpawnRadius] around the player.
        var angle = _random.NextFloat() * MathF.Tau;
        var dist = _random.NextFloat(ent.Comp.RampMinSpawnDistance, ent.Comp.RampSpawnRadius);
        var spawnWorld = anchorPos + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);

        var groupCap = ent.Comp.RampStartGroup
            + (int)MathF.Round(ent.Comp.RampMaxExtraGroup * GetRampProgress(ent.Comp, now));
        var groupSize = _random.Next(ent.Comp.RampStartGroup, Math.Max(ent.Comp.RampStartGroup + 1, groupCap + 1));

        var mobProto = _random.Pick(ent.Comp.RampingMobs);
        var room = ent.Comp.RampLiveCap - ent.Comp.LiveRampMobs.Count;
        groupSize = Math.Min(groupSize, room);

        // Check the pack's anchor tile is clear; bail this tick rather than
        // spawning the whole pack inside a wall/rock cluster.
        var planetGrid = CompOrNull<MapGridComponent>(ent.Owner);
        var planetBiome = CompOrNull<BiomeComponent>(ent.Owner);
        if (planetGrid != null)
        {
            var anchorTile = new Vector2i((int)MathF.Floor(spawnWorld.X), (int)MathF.Floor(spawnWorld.Y));
            if (IsTileBlockedForMob(ent.Owner, planetGrid, planetBiome, anchorTile))
                return;
        }

        for (var i = 0; i < groupSize; i++)
        {
            // Light per-mob jitter so the pack doesn't stack on one tile.
            var jitter = new Vector2(_random.NextFloat(-1.5f, 1.5f), _random.NextFloat(-1.5f, 1.5f));
            var coords = new MapCoordinates(spawnWorld + jitter, planetMapId);
            var mob = Spawn(mobProto, coords);
            ent.Comp.LiveRampMobs.Add(mob);
        }
    }

    /// <summary>
    /// Returns true when the candidate world-tile position is clear of all
    /// known activity sources: existing dungeons, player plot grids, and
    /// shuttles currently on the planet map. Operates on a pre-collected
    /// snapshot so the same lists are reused across all candidates in a roll.
    /// </summary>
    private static bool IsCandidatePositionClear(Vector2 worldPos, PlanetSpawnerComponent spawner,
        List<Vector2> plotPositions, List<Vector2> shuttlePositions)
    {
        // Dungeon-vs-dungeon spacing.
        var dungeonDistSq = spawner.MinDistBetweenDungeons * spawner.MinDistBetweenDungeons;
        foreach (var existing in spawner.SpawnedDungeons)
        {
            if ((worldPos - existing).LengthSquared() < dungeonDistSq)
                return false;
        }

        var activityDistSq = spawner.MinDistFromActivity * spawner.MinDistFromActivity;

        foreach (var pos in plotPositions)
        {
            if ((worldPos - pos).LengthSquared() < activityDistSq)
                return false;
        }

        foreach (var pos in shuttlePositions)
        {
            if ((worldPos - pos).LengthSquared() < activityDistSq)
                return false;
        }

        return true;
    }
}
