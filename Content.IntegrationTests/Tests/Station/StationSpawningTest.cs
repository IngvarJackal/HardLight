using System.Linq;
using Content.Server.Maps;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared.Preferences;
using Content.Shared.Station.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Station;

[TestFixture]
[TestOf(typeof(StationSpawningSystem))]
public sealed class StationSpawningTest
{
    [TestPrototypes]
    private const string Prototypes =
        "- type: vessel\n"
        + "  id: TestNoSpawnHiringVessel\n"
        + "  parent: BaseVessel\n"
        + "  name: Test No Spawn Hiring Vessel\n"
        + "  description: Test vessel without spawn markers.\n"
        + "  price: 1000\n"
        + "  category: Small\n"
        + "  group: Shipyard\n"
        + "  shuttlePath: /Maps/Test/empty.yml\n"
        + "  class:\n"
        + "  - Civilian\n"
        + "  engine:\n"
        + "  - Uranium\n"
        + "\n"
        + "- type: gameMap\n"
        + "  id: TestNoSpawnShipStation\n"
        + "  minPlayers: 0\n"
        + "  mapName: TestNoSpawnShipStation\n"
        + "  mapPath: /Maps/Test/empty.yml\n"
        + "  stations:\n"
        + "    Station:\n"
        + "      mapNameTemplate: TestNoSpawnShipStation\n"
        + "      stationProto: StandardFrontierVessel\n"
        + "      components:\n"
        + "        - type: ExtraShuttleInformation\n"
        + "          vessel: TestNoSpawnHiringVessel\n"
        + "        - type: StationJobs\n"
        + "          availableJobs:\n"
        + "            Mercenary: [0, 1]\n";

    [Test]
    public async Task ShipInterviewLateJoinFallbackStaysOnSelectedGridTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var entitySystemManager = server.ResolveDependency<IEntitySystemManager>();
        var mapLoader = entitySystemManager.GetEntitySystem<MapLoaderSystem>();
        var stationSpawning = entitySystemManager.GetEntitySystem<StationSpawningSystem>();
        var stationSystem = entitySystemManager.GetEntitySystem<StationSystem>();

        var station = EntityUid.Invalid;
        var gridUid = EntityUid.Invalid;
        var spawned = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var shipProto = prototypeManager.Index<GameMapPrototype>("TestNoSpawnShipStation");

            Assert.That(mapLoader.TryLoadMap(new ResPath("/Maps/Test/empty.yml"), out _, out var grids), Is.True);
            Assert.That(grids, Is.Not.Null);

            gridUid = grids!.First().Owner;
            station = stationSystem.InitializeNewStation(shipProto.Stations["Station"], new[] { gridUid }, "No Spawn Ship");
            entityManager.EnsureComponent<StationMemberComponent>(gridUid).Station = station;

            spawned = stationSpawning.SpawnPlayerCharacterOnStation(
                    station,
                    StationJobsSystem.ShipFreelancerInterviewJobId,
                    HumanoidCharacterProfile.Random(),
                    spawnPointType: SpawnPointType.LateJoin)
                ?? EntityUid.Invalid;
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(spawned, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(entityManager.GetComponent<TransformComponent>(spawned).GridUid, Is.EqualTo(gridUid));
        });

        await pair.CleanReturnAsync();
    }
}
