using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Tests._NF;
using Content.Server._HL.ColComm;
using Content.Server.GameTicking;
using Content.Server._NF.CryoSleep;
using Content.Server._NF.RoundNotifications.Events;
using Content.Server._NF.Roles.Systems;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Content.Shared.HL.CCVar;

namespace Content.IntegrationTests.Tests.Station;

[TestFixture]
[TestOf(typeof(StationJobsSystem))]
public sealed class StationJobsTest
{
    [TestPrototypes]
    private const string Prototypes = BasePrototypes + TrackedCrewPrototype + ShipJobPrototypes + DynamicAllocationPrototypes;

    private const string BasePrototypes = @"
- type: playTimeTracker
  id: PlayTimeDummyAssistant

- type: playTimeTracker
  id: PlayTimeDummyMime

- type: playTimeTracker
  id: PlayTimeDummyClown

- type: playTimeTracker
  id: PlayTimeDummyCaptain

- type: playTimeTracker
  id: PlayTimeDummyChaplain

- type: gameMap
  id: FooStation
  minPlayers: 0
  mapName: FooStation
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: FooStation
      stationProto: StandardNanotrasenStation
      components:
        - type: StationJobs
          availableJobs:
            TMime: [0, -1]
            TAssistant: [-1, -1]
            TCaptain: [5, 5]
            TClown: [5, 6]

- type: job
  id: TAssistant
  playTimeTracker: PlayTimeDummyAssistant

- type: job
  id: TMime
  weight: 20
  playTimeTracker: PlayTimeDummyMime

- type: job
  id: TClown
  weight: -10
  playTimeTracker: PlayTimeDummyClown

- type: job
  id: TCaptain
  weight: 10
  playTimeTracker: PlayTimeDummyCaptain

- type: job
  id: TChaplain
  playTimeTracker: PlayTimeDummyChaplain
";

    private const string TrackedCrewPrototype =
        "- type: entity\n"
        + "  id: TestTrackedCrewMob\n"
        + "  components:\n"
        + "  - type: MindContainer\n"
        + "  - type: DoAfter\n"
        + "  - type: Damageable\n"
        + "    damageContainer: Biological\n"
        + "  - type: Body\n"
        + "    prototype: Human\n"
        + "    requiredLegs: 2\n"
        + "  - type: MobState\n"
        + "  - type: MobThresholds\n"
        + "    thresholds:\n"
        + "      0: Alive\n"
        + "      200: Dead\n";

        private const string ShipJobPrototypes =
                "- type: vessel\n"
                + "  id: TestHiringVessel\n"
                + "  parent: BaseVessel\n"
                + "  name: Test Hiring Vessel\n"
                + "  description: Test hiring vessel.\n"
                + "  price: 1000\n"
                + "  category: Small\n"
                + "  group: Shipyard\n"
                + "  shuttlePath: /Maps/Test/empty.yml\n"
                + "  class:\n"
                + "  - Civilian\n"
                + "  engine:\n"
                + "  - Uranium\n"
                + "\n"
                + "- type: vessel\n"
                + "  id: TestCargoVessel\n"
                + "  parent: BaseVessel\n"
                + "  name: Test Cargo Vessel\n"
                + "  description: Test cargo vessel.\n"
                + "  price: 1000\n"
                + "  category: Small\n"
                + "  group: Shipyard\n"
                + "  shuttlePath: /Maps/Test/empty.yml\n"
                + "  class:\n"
                + "  - Cargo\n"
                + "  engine:\n"
                + "  - Uranium\n"
                + "\n"
                + "- type: vessel\n"
                + "  id: TestBusVessel\n"
                + "  parent: BaseVesselBus\n"
                + "  shuttlePath: /Maps/Test/empty.yml\n"
                + "\n"
                + "- type: gameMap\n"
                + "  id: TestHiringShipStation\n"
                + "  minPlayers: 0\n"
                + "  mapName: TestHiringShipStation\n"
                + "  mapPath: /Maps/Test/empty.yml\n"
                + "  stations:\n"
                + "    Station:\n"
                + "      mapNameTemplate: TestHiringShipStation\n"
                + "      stationProto: StandardFrontierVessel\n"
                + "      components:\n"
                + "        - type: ExtraShuttleInformation\n"
                + "          vessel: TestHiringVessel\n"
                + "        - type: StationJobs\n"
                + "          availableJobs:\n"
                + "            Mercenary: [0, 2]\n"
                + "            ContractorInterview: [0, 1]\n"
                + "            PilotInterview: [0, 1]\n"
                + "\n"
                + "- type: gameMap\n"
                + "  id: TestCargoShipStation\n"
                + "  minPlayers: 0\n"
                + "  mapName: TestCargoShipStation\n"
                + "  mapPath: /Maps/Test/empty.yml\n"
                + "  stations:\n"
                + "    Station:\n"
                + "      mapNameTemplate: TestCargoShipStation\n"
                + "      stationProto: StandardFrontierVessel\n"
                + "      components:\n"
                + "        - type: ExtraShuttleInformation\n"
                + "          vessel: TestCargoVessel\n"
                + "        - type: StationJobs\n"
                + "          availableJobs:\n"
                + "            MercenaryInterview: [0, 2]\n"
                + "\n"
                + "- type: gameMap\n"
                + "  id: TestSingleSlotShipStation\n"
                + "  minPlayers: 0\n"
                + "  mapName: TestSingleSlotShipStation\n"
                + "  mapPath: /Maps/Test/empty.yml\n"
                + "  stations:\n"
                + "    Station:\n"
                + "      mapNameTemplate: TestSingleSlotShipStation\n"
                + "      stationProto: StandardFrontierVessel\n"
                + "      components:\n"
                + "        - type: ExtraShuttleInformation\n"
                + "          vessel: TestHiringVessel\n"
                + "        - type: StationJobs\n"
                + "          availableJobs:\n"
                + "            Mercenary: [0, 1]\n"
                + "\n"
                + "- type: gameMap\n"
                + "  id: TestBusShipStation\n"
                + "  minPlayers: 0\n"
                + "  mapName: TestBusShipStation\n"
                + "  mapPath: /Maps/Test/empty.yml\n"
                + "  stations:\n"
                + "    Station:\n"
                + "      mapNameTemplate: TestBusShipStation\n"
                + "      stationProto: StandardFrontierBusVessel\n"
                + "      components:\n"
                + "        - type: ExtraShuttleInformation\n"
                + "          vessel: TestBusVessel\n"
                + "        - type: StationJobs\n"
                + "          availableJobs:\n"
                + "            MercenaryInterview: [0, 2]\n";

    private const string DynamicAllocationPrototypes =
                "- type: gameMap\n"
                + "  id: TestDynamicAllocationStation\n"
                + "  minPlayers: 0\n"
                + "  mapName: TestDynamicAllocationStation\n"
                + "  mapPath: /Maps/Test/empty.yml\n"
                + "  stations:\n"
                + "    Station:\n"
                + "      mapNameTemplate: TestDynamicAllocationStation\n"
                + "      stationProto: StandardNanotrasenStation\n"
                + "      components:\n"
                + "        - type: StationJobs\n"
                + "          availableJobs:\n"
                + "            Mercenary: [0, 40]\n";

    private const int StationCount = 100;
    private const int CaptainCount = StationCount;
    private const int PlayerCount = 2000;
    private const int TotalPlayers = PlayerCount + CaptainCount;

    [Test]
    public async Task AssignJobsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var fooStationProto = prototypeManager.Index<GameMapPrototype>("FooStation");
        var entSysMan = server.ResolveDependency<IEntityManager>().EntitySysManager;
        var stationJobs = entSysMan.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var logmill = server.ResolveDependency<ILogManager>().RootSawmill;

        List<EntityUid> stations = new();
        await server.WaitPost(() =>
        {
            for (var i = 0; i < StationCount; i++)
            {
                stations.Add(stationSystem.InitializeNewStation(fooStationProto.Stations["Station"], null, $"Foo {StationCount}"));
            }
        });

        await server.WaitAssertion(() =>
        {
            var fakePlayers = new Dictionary<NetUserId, HumanoidCharacterProfile>()
                .AddJob("TAssistant", JobPriority.Medium, PlayerCount)
                .AddPreference("TClown", JobPriority.Low)
                .AddPreference("TMime", JobPriority.High)
                .WithPlayers(
                    new Dictionary<NetUserId, HumanoidCharacterProfile>()
                    .AddJob("TCaptain", JobPriority.High, CaptainCount)
                );
            Assert.That(fakePlayers, Is.Not.Empty);

            var start = new Stopwatch();
            start.Start();
            var assigned = stationJobs.AssignJobs(fakePlayers, stations);
            Assert.That(assigned, Is.Not.Empty);
            var time = start.Elapsed.TotalMilliseconds;
            logmill.Info($"Took {time} ms to distribute {TotalPlayers} players.");

            Assert.Multiple(() =>
            {
                foreach (var station in stations)
                {
                    var assignedHere = assigned
                        .Where(x => x.Value.Item2 == station)
                        .ToDictionary(x => x.Key, x => x.Value);

                    // Each station should have SOME players.
                    Assert.That(assignedHere, Is.Not.Empty);
                    // And it should have at least the minimum players to be considered a "fair" share, as they're all the same.
                    Assert.That(assignedHere, Has.Count.GreaterThanOrEqualTo(TotalPlayers / stations.Count), "Station has too few players.");
                    // And it shouldn't have ALL the players, either.
                    Assert.That(assignedHere, Has.Count.LessThan(TotalPlayers), "Station has too many players.");
                    // And there should be *A* captain, as there's one player with captain enabled per station.
                    Assert.That(assignedHere.Where(x => x.Value.Item1 == "TCaptain").ToList(), Has.Count.EqualTo(1));
                }

                // All clown players have assistant as a higher priority.
                Assert.That(assigned.Values.Select(x => x.Item1).ToList(), Does.Not.Contain("TClown"));
                // Mime isn't an open job-slot at round-start.
                Assert.That(assigned.Values.Select(x => x.Item1).ToList(), Does.Not.Contain("TMime"));
                // All players have slots they can fill.
                Assert.That(assigned.Values, Has.Count.EqualTo(TotalPlayers), $"Expected {TotalPlayers} players.");
                // There must be assistants present.
                Assert.That(assigned.Values.Select(x => x.Item1).ToList(), Does.Contain("TAssistant"));
                // There must be captains present, too.
                Assert.That(assigned.Values.Select(x => x.Item1).ToList(), Does.Contain("TCaptain"));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdjustJobsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var fooStationProto = prototypeManager.Index<GameMapPrototype>("FooStation");
        var entSysMan = server.ResolveDependency<IEntityManager>().EntitySysManager;
        var stationJobs = entSysMan.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();

        var station = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            station = stationSystem.InitializeNewStation(fooStationProto.Stations["Station"], null, $"Foo Station");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            // Verify jobs are/are not unlimited.
            Assert.Multiple(() =>
            {
                Assert.That(stationJobs.IsJobUnlimited(station, "TAssistant"), "TAssistant is expected to be unlimited.");
                Assert.That(stationJobs.IsJobUnlimited(station, "TMime"), "TMime is expected to be unlimited.");
                Assert.That(!stationJobs.IsJobUnlimited(station, "TCaptain"), "TCaptain is expected to not be unlimited.");
                Assert.That(!stationJobs.IsJobUnlimited(station, "TClown"), "TClown is expected to not be unlimited.");
            });
            Assert.Multiple(() =>
            {
                Assert.That(stationJobs.TrySetJobSlot(station, "TClown", 0), "Could not set TClown to have zero slots.");
                Assert.That(stationJobs.TryGetJobSlot(station, "TClown", out var clownSlots), "Could not get the number of TClown slots.");
                Assert.That(clownSlots, Is.EqualTo(0));
                Assert.That(!stationJobs.TryAdjustJobSlot(station, "TCaptain", -9999), "Was able to adjust TCaptain by -9999 without clamping.");
                Assert.That(stationJobs.TryAdjustJobSlot(station, "TCaptain", -9999, false, true), "Could not adjust TCaptain by -9999.");
                Assert.That(stationJobs.TryGetJobSlot(station, "TCaptain", out var captainSlots), "Could not get the number of TCaptain slots.");
                Assert.That(captainSlots, Is.EqualTo(0));
            });
            Assert.Multiple(() =>
            {
                Assert.That(stationJobs.TrySetJobSlot(station, "TChaplain", 10, true), "Could not create 10 TChaplain slots.");
                stationJobs.MakeJobUnlimited(station, "TChaplain");
                Assert.That(stationJobs.IsJobUnlimited(station, "TChaplain"), "Could not make TChaplain unlimited.");
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LateJoinListingsIgnoreJobsMissingFromColcommRegistryTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var fooStationProto = prototypeManager.Index<GameMapPrototype>("FooStation");
        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var colcommJobs = entSysMan.GetEntitySystem<ColcommJobSystem>();
        var stationJobs = entSysMan.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();

        var registryUid = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            registryUid = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
            var registry = entityManager.EnsureComponent<ColcommJobRegistryComponent>(registryUid);
            SeedColcommRegistry(registry);

            station = stationSystem.InitializeNewStation(fooStationProto.Stations["Station"], null, "Foo Latejoin Station");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(colcommJobs.TryGetColcommRegistry(out var colcomm), Is.True);
            Assert.That(colcommJobs.TryGetJobSlot(colcomm, "TAssistant", out _), Is.False);
            Assert.That(colcommJobs.TryGetJobSlot(colcomm, "TCaptain", out _), Is.False);
            Assert.That(stationJobs.GetAvailableJobs(station), Is.Empty);
            Assert.That(stationJobs.GetJobs(station).Count, Is.EqualTo(4)); // HL: The FooStation has 4 jobs, not 0
        });

        await server.WaitPost(() =>
        {
            entityManager.DeleteEntity(registryUid);
        });

        await server.WaitRunTicks(1);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InvalidRoundstartJobsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var compFact = server.ResolveDependency<IComponentFactory>();
        var name = compFact.GetComponentName<StationJobsComponent>();

        await server.WaitAssertion(() =>
        {
            // invalidJobs contains all the jobs which can't be set for preference.
            // These may still exist for latejoin/midround availability, but they should never have roundstart slots.
            var invalidJobs = new HashSet<string>();
            foreach (var job in prototypeManager.EnumeratePrototypes<JobPrototype>())
            {
                if (!job.SetPreference)
                    invalidJobs.Add(job.ID);
            }

            Assert.Multiple(() =>
            {
                foreach (var mapProto in FrontierConstants.GameMapPrototypes) // Frontier: EnumeratePrototypes<GameMapPrototype> < FrontierConstants.GameMapPrototypes
                {
                    // Frontier: get prototype from proto ID
                    if (!prototypeManager.TryIndex<GameMapPrototype>(mapProto, out var gameMap))
                    {
                        Assert.Fail($"Could not find GameMapPrototype with ID {mapProto}! Is FrontierConstants up to date?");
                    }
                    // End Frontier

                    foreach (var (stationId, station) in gameMap.Stations)
                    {
                        if (!station.StationComponentOverrides.TryGetComponent(name, out var comp))
                            continue;

                        foreach (var (job, array) in ((StationJobsComponent) comp).SetupAvailableJobs)
                        {
                            Assert.That(array.Length, Is.EqualTo(2));
                            Assert.That(array[0] is -1 or >= 0);
                            Assert.That(array[1] is -1 or >= 0);

                            if (invalidJobs.Contains(job))
                            {
                                Assert.That(array[0], Is.EqualTo(0), $"Station {stationId} contains non-preference job prototype {job} with roundstart slots {array[0]}.");
                            }
                        }
                    }
                }
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipCrewHiringEligibilityTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Fresh = true,
            Destructive = true
        });
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var hiringShipProto = prototypeManager.Index<GameMapPrototype>("TestHiringShipStation");
        var cargoShipProto = prototypeManager.Index<GameMapPrototype>("TestCargoShipStation");
        var busShipProto = prototypeManager.Index<GameMapPrototype>("TestBusShipStation");
        var entSysMan = server.ResolveDependency<IEntityManager>().EntitySysManager;
        var stationJobs = entSysMan.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();

        var hiringStation = EntityUid.Invalid;
        var cargoStation = EntityUid.Invalid;
        var busStation = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            hiringStation = stationSystem.InitializeNewStation(hiringShipProto.Stations["Station"], null, "Hiring Ship");
            cargoStation = stationSystem.InitializeNewStation(cargoShipProto.Stations["Station"], null, "Cargo Ship");
            busStation = stationSystem.InitializeNewStation(busShipProto.Stations["Station"], null, "Bus Ship");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            ProtoId<JobPrototype> freelancerInterview = StationJobsSystem.ShipFreelancerInterviewJobId;
            ProtoId<JobPrototype> pilotInterview = "PilotInterview";
            ProtoId<JobPrototype> contractorInterview = "ContractorInterview";
            ProtoId<JobPrototype> mercenary = "Mercenary";

            Assert.Multiple(() =>
            {
                Assert.That(stationJobs.IsShipCrewHiringStation(hiringStation), Is.True);
                Assert.That(stationJobs.IsShipCrewHiringStation(cargoStation), Is.False);
                Assert.That(stationJobs.IsShipCrewHiringStation(busStation), Is.False);

                // Only the Freelancer interview is exposed as a late-join lobby job; Pilot/Contractor
                // interview slots exist on the station for in-round assignment but must not be advertised.
                var hiringJobs = stationJobs.GetJobs(hiringStation);
                Assert.That(hiringJobs.Keys, Is.EquivalentTo(new[] { freelancerInterview }));
                Assert.That(hiringJobs[freelancerInterview], Is.EqualTo(2));
                Assert.That(hiringJobs.ContainsKey(mercenary), Is.False);
                Assert.That(stationJobs.IsAdvertisedLateJoinJob(hiringStation, freelancerInterview), Is.True);
                Assert.That(stationJobs.IsAdvertisedLateJoinJob(hiringStation, pilotInterview), Is.False);
                Assert.That(stationJobs.IsAdvertisedLateJoinJob(hiringStation, contractorInterview), Is.False);
                Assert.That(stationJobs.IsAdvertisedLateJoinJob(hiringStation, mercenary), Is.False);

                Assert.That(stationJobs.GetStationTrackingJobId(hiringStation, mercenary), Is.EqualTo(freelancerInterview));
                Assert.That(stationJobs.GetStationTrackingJobId(hiringStation, "Pilot"), Is.EqualTo(pilotInterview));
                Assert.That(stationJobs.GetStationTrackingJobId(hiringStation, "Contractor"), Is.EqualTo(contractorInterview));

                Assert.That(stationJobs.GetJobs(cargoStation), Is.Empty);
                Assert.That(stationJobs.GetJobs(busStation), Is.Empty);
                Assert.That(stationJobs.GetStationTrackingJobId(cargoStation, mercenary), Is.EqualTo(mercenary));
                Assert.That(stationJobs.GetStationTrackingJobId(busStation, mercenary), Is.EqualTo(mercenary));
            });

            var firstUser = new NetUserId(Guid.NewGuid());
            Assert.That(stationJobs.TryAssignJob(hiringStation, freelancerInterview, firstUser), Is.True);
            Assert.That(stationJobs.TryGetJobSlot(hiringStation, freelancerInterview, out var hiringSlots), Is.True);
            Assert.That(hiringSlots, Is.EqualTo(1));

            var secondUser = new NetUserId(Guid.NewGuid());
            Assert.That(stationJobs.TryAssignJob(cargoStation, freelancerInterview, secondUser), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClosingFilledShipFreelancerSlotStaysClosedOnReopenTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        ProtoId<GameMapPrototype> singleSlotShipId = "TestSingleSlotShipStation";
        var singleSlotShipProto = prototypeManager.Index(singleSlotShipId);
        var entMan = server.ResolveDependency<IEntityManager>();
        var entSysMan = entMan.EntitySysManager;
        var stationJobs = entSysMan.GetEntitySystem<StationJobsSystem>();
        var jobTracking = entSysMan.GetEntitySystem<JobTrackingSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();

        var station = EntityUid.Invalid;
        var trackedCrew = EntityUid.Invalid;
        var trackedUser = new NetUserId(Guid.NewGuid());

        await server.WaitPost(() =>
        {
            station = stationSystem.InitializeNewStation(singleSlotShipProto.Stations["Station"], null, "Single Slot Ship");
            trackedCrew = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            jobTracking.EnsureTrackedJob(trackedCrew, "Mercenary", station);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            ProtoId<JobPrototype> freelancerInterview = StationJobsSystem.ShipFreelancerInterviewJobId;

            Assert.That(stationJobs.TryAssignJob(station, freelancerInterview, trackedUser), Is.True);
            Assert.That(stationJobs.TryGetJobSlot(station, freelancerInterview, out var initialSlots), Is.True);
            Assert.That(initialSlots, Is.EqualTo(0));
            Assert.That(stationJobs.IsPlayerJobTracked(station, trackedUser, freelancerInterview), Is.True);

            Assert.That(stationJobs.TryAdjustJobCapacity(station, freelancerInterview, -1, clamp: true), Is.True);
            Assert.That(stationJobs.TryGetJobMidRoundMax(station, freelancerInterview, out var configuredMax), Is.True);
            Assert.That(configuredMax, Is.EqualTo(0));

            var tracking = entMan.GetComponent<Content.Shared._NF.Roles.Components.JobTrackingComponent>(trackedCrew);
            jobTracking.OpenJob((trackedCrew, tracking), trackedUser);

            Assert.That(stationJobs.IsPlayerJobTracked(station, trackedUser, freelancerInterview), Is.False);
            Assert.That(stationJobs.TryGetJobSlot(station, freelancerInterview, out var reopenedSlots), Is.True);
            Assert.That(reopenedSlots, Is.EqualTo(0));
            Assert.That(stationJobs.TryGetJobMidRoundMax(station, freelancerInterview, out configuredMax), Is.True);
            Assert.That(configuredMax, Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CryoTrackedJobReopensSlotWithoutDeletingEntityTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var entitySystemManager = server.ResolveDependency<IEntitySystemManager>();
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var cryoSleep = entitySystemManager.GetEntitySystem<CryoSleepSystem>();
        var mapLoader = entitySystemManager.GetEntitySystem<MapLoaderSystem>();
        var mindSystem = entitySystemManager.GetEntitySystem<SharedMindSystem>();
        var jobTracking = entitySystemManager.GetEntitySystem<JobTrackingSystem>();
        var stationJobs = entitySystemManager.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entitySystemManager.GetEntitySystem<StationSystem>();

        var stationProto = prototypeManager.Index<GameMapPrototype>("FooStation");
        var serverSession = playerManager.Sessions.Single();
        var trackedUser = serverSession.UserId;

        var station = EntityUid.Invalid;
        var gridUid = EntityUid.Invalid;
        var trackedCrew = EntityUid.Invalid;
        var cryopod = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            Assert.That(mapLoader.TryLoadMap(new ResPath("/Maps/Test/empty.yml"), out _, out var grids), Is.True);
            Assert.That(grids, Is.Not.Null);

            gridUid = grids!.First().Owner;
            station = stationSystem.InitializeNewStation(stationProto.Stations["Station"], new[] { gridUid }, "Tracked Cryo Station");
            entityManager.EnsureComponent<StationMemberComponent>(gridUid).Station = station;

            trackedCrew = entityManager.SpawnEntity("TestTrackedCrewMob", new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)));
            cryopod = entityManager.SpawnEntity("MachineCryoSleepPod", new EntityCoordinates(gridUid, new Vector2(1.5f, 0.5f)));

            jobTracking.EnsureTrackedJob(trackedCrew, "TCaptain", station);

            var mind = mindSystem.CreateMind(trackedUser);
            mindSystem.TransferTo(mind, trackedCrew, mind: mind);
            playerManager.SetAttachedEntity(serverSession, trackedCrew);

            Assert.That(stationJobs.TryAssignJob(station, "TCaptain", trackedUser), Is.True);
            Assert.That(cryoSleep.InsertBody(trackedCrew, (cryopod, entityManager.GetComponent<CryoSleepComponent>(cryopod)), false), Is.True);

            cryoSleep.CryoStoreBody(trackedCrew, cryopod);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(trackedCrew), Is.True);
            Assert.That(stationJobs.IsPlayerJobTracked(station, trackedUser, "TCaptain"), Is.False);
            Assert.That(stationJobs.TryGetJobSlot(station, "TCaptain", out var slots), Is.True);
            Assert.That(slots, Is.EqualTo(5));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipLegacyInterviewJobsReopenWhenStationMapsThemTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var entSysMan = server.ResolveDependency<IEntityManager>().EntitySysManager;
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var stationJobs = entSysMan.GetEntitySystem<StationJobsSystem>();
        var jobTracking = entSysMan.GetEntitySystem<JobTrackingSystem>();

        var hiringShipProto = prototypeManager.Index<GameMapPrototype>("TestHiringShipStation");
        var cargoShipProto = prototypeManager.Index<GameMapPrototype>("TestCargoShipStation");

        var hiringStation = EntityUid.Invalid;
        var cargoStation = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            hiringStation = stationSystem.InitializeNewStation(hiringShipProto.Stations["Station"], null, "Hiring Ship");
            cargoStation = stationSystem.InitializeNewStation(cargoShipProto.Stations["Station"], null, "Cargo Ship");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(InvokeShouldReopenTrackedJob(jobTracking, hiringStation, "Mercenary"), Is.True);
                Assert.That(InvokeShouldReopenTrackedJob(jobTracking, hiringStation, "Pilot"), Is.True);
                Assert.That(InvokeShouldReopenTrackedJob(jobTracking, hiringStation, "Contractor"), Is.True);
                Assert.That(InvokeShouldReopenTrackedJob(jobTracking, cargoStation, "Mercenary"), Is.False);
                Assert.That(InvokeShouldReopenTrackedJob(jobTracking, cargoStation, "Pilot"), Is.False);
                Assert.That(InvokeShouldReopenTrackedJob(jobTracking, cargoStation, "Contractor"), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrewLatejoinLobbyOnlyShowsShipsWithConsoleAndOpenSlotsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var configManager = server.ResolveDependency<IConfigurationManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var entitySystemManager = server.ResolveDependency<IEntitySystemManager>();
        var mapLoader = entitySystemManager.GetEntitySystem<MapLoaderSystem>();
        var stationJobs = entitySystemManager.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entitySystemManager.GetEntitySystem<StationSystem>();

        var station = EntityUid.Invalid;
        var gridUid = EntityUid.Invalid;
        var console = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            configManager.SetCVar(CCVars.GameDisallowLateJoins, false);

            var shipProto = prototypeManager.Index<GameMapPrototype>("TestHiringShipStation");
            Assert.That(mapLoader.TryLoadMap(new ResPath("/Maps/Test/empty.yml"), out _, out var grids), Is.True);
            Assert.That(grids, Is.Not.Null);

            gridUid = grids!.First().Owner;
            station = stationSystem.InitializeNewStation(shipProto.Stations["Station"], new[] { gridUid }, "Lobby Ship");
            entityManager.EnsureComponent<StationMemberComponent>(gridUid).Station = station;
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var jobsEvent = InvokeGenerateJobsAvailableEvent(stationJobs);
            Assert.That(jobsEvent.StationJobList.ContainsKey(entityManager.GetNetEntity(station)), Is.False);
        });

        await server.WaitPost(() =>
        {
            console = entityManager.SpawnEntity("ComputerStationRecords", new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)));
            ForceGridUid(entityManager.GetComponent<TransformComponent>(console), gridUid);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.GetComponent<TransformComponent>(console).GridUid, Is.EqualTo(gridUid));
            Assert.That(stationSystem.GetOwningStation(console), Is.EqualTo(station));

            var stationsWithConsole = InvokeGetStationsWithCrewRecordsConsole(stationJobs);
            Assert.That(stationsWithConsole, Contains.Item(station));

            var jobsEvent = InvokeGenerateJobsAvailableEvent(stationJobs);
            var netStation = entityManager.GetNetEntity(station);

            Assert.That(jobsEvent.StationJobList.ContainsKey(netStation), Is.True);
            Assert.That(jobsEvent.StationJobList[netStation].JobsAvailable.TryGetValue(StationJobsSystem.ShipFreelancerInterviewJobId, out var slots), Is.True);
            Assert.That(slots, Is.EqualTo(2));
        });

        await server.WaitPost(() =>
        {
            Assert.That(stationJobs.TryAdjustJobCapacity(station, StationJobsSystem.ShipFreelancerInterviewJobId, -2, clamp: true), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var jobsEvent = InvokeGenerateJobsAvailableEvent(stationJobs);
            Assert.That(jobsEvent.StationJobList.ContainsKey(entityManager.GetNetEntity(station)), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DynamicJobAllocationClampsMercenarySlotsWhenEmergencyShuttleDisabledTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var configManager = server.ResolveDependency<IConfigurationManager>();
        var entitySystemManager = server.ResolveDependency<IEntitySystemManager>();
        var colcommJobs = entitySystemManager.GetEntitySystem<ColcommJobSystem>();
        var gameTicker = entitySystemManager.GetEntitySystem<GameTicker>();
        var stationJobs = entitySystemManager.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entitySystemManager.GetEntitySystem<StationSystem>();
        var station = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            configManager.SetCVar(CCVars.EmergencyShuttleEnabled, false);
            configManager.SetCVar(HLCCVars.AutoSpawnColComm, true);

            var stationProto = prototypeManager.Index<GameMapPrototype>("TestDynamicAllocationStation");
            station = stationSystem.InitializeNewStation(stationProto.Stations["Station"], null, "Dynamic Allocation Station");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(colcommJobs.TryGetColcommRegistry(out var colcomm), Is.True);
            Assert.That(colcommJobs.IsConfiguredJob(colcomm, "Mercenary"), Is.True);
            Assert.That(colcommJobs.TryGetJobSlot(colcomm, "Mercenary", out var beforeSlots), Is.True);
            Assert.That(beforeSlots, Is.EqualTo(40));
        });

        await server.WaitPost(() =>
        {
            Assert.That(gameTicker.StartGameRule("DynamicJobAllocation", out _), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(colcommJobs.TryGetColcommRegistry(out var colcomm), Is.True);
            Assert.That(colcommJobs.TryGetJobSlot(colcomm, "Mercenary", out var colcommSlots), Is.True);
            Assert.That(colcommSlots, Is.EqualTo(0));

            var stationList = stationJobs.GetJobs(station);
            Assert.That(stationList.TryGetValue("Mercenary", out var stationSlots), Is.True);
            Assert.That(stationSlots, Is.EqualTo(0));

            var jobsEvent = InvokeGenerateJobsAvailableEvent(stationJobs);
            var stationNet = server.ResolveDependency<IEntityManager>().GetNetEntity(station);
            Assert.That(jobsEvent.StationJobList.TryGetValue(stationNet, out var stationInfo), Is.True);
            Assert.That(stationInfo!.JobsAvailable.TryGetValue("Mercenary", out var lobbySlots), Is.True);
            Assert.That(lobbySlots, Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test] // HardLight
    public async Task RoundRestartStationDeletionDoesNotOpenTrackedJobsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Fresh = true,
            Destructive = true
        });
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var entitySystemManager = entityManager.EntitySysManager;
        var stationSystem = entitySystemManager.GetEntitySystem<StationSystem>();
        var jobTracking = entitySystemManager.GetEntitySystem<JobTrackingSystem>();

        var stationProto = prototypeManager.Index<GameMapPrototype>("FooStation");

        var regularDeletionStation = EntityUid.Invalid;
        var regularDeletionCrew = EntityUid.Invalid;
        var restartDeletionStation = EntityUid.Invalid;
        var restartDeletionCrew = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            regularDeletionStation = stationSystem.InitializeNewStation(stationProto.Stations["Station"], null, "Regular Delete Station");
            regularDeletionCrew = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
            jobTracking.EnsureTrackedJob(regularDeletionCrew, "TCaptain", regularDeletionStation);

            restartDeletionStation = stationSystem.InitializeNewStation(stationProto.Stations["Station"], null, "Restart Delete Station");
            restartDeletionCrew = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
            jobTracking.EnsureTrackedJob(restartDeletionCrew, "TCaptain", restartDeletionStation);
        });

        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            stationSystem.DeleteStation(regularDeletionStation);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(regularDeletionCrew), Is.True);
            Assert.That(entityManager.GetComponent<Content.Shared._NF.Roles.Components.JobTrackingComponent>(regularDeletionCrew).Active, Is.False);
        });

        await server.WaitPost(() =>
        {
            entityManager.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
            stationSystem.DeleteStation(restartDeletionStation);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(restartDeletionCrew), Is.True);
            Assert.That(entityManager.GetComponent<Content.Shared._NF.Roles.Components.JobTrackingComponent>(restartDeletionCrew).Active, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test] // HardLight
    public async Task DeletingTrackedBodyReopensStationSlotTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;

        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var entitySystemManager = server.ResolveDependency<IEntitySystemManager>();
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var jobTracking = entitySystemManager.GetEntitySystem<JobTrackingSystem>();
        var mindSystem = entitySystemManager.GetEntitySystem<SharedMindSystem>();
        var stationJobs = entitySystemManager.GetEntitySystem<StationJobsSystem>();
        var stationSystem = entitySystemManager.GetEntitySystem<StationSystem>();

        var stationProto = prototypeManager.Index<GameMapPrototype>("FooStation");
        var serverSession = playerManager.Sessions.Single();
        var trackedUser = serverSession.UserId;

        var station = EntityUid.Invalid;
        var trackedCrew = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            station = stationSystem.InitializeNewStation(stationProto.Stations["Station"], null, "Delete Tracked Body Station");
            trackedCrew = entityManager.SpawnEntity("TestTrackedCrewMob", MapCoordinates.Nullspace);

            jobTracking.EnsureTrackedJob(trackedCrew, "TCaptain", station);

            var mind = mindSystem.CreateMind(trackedUser);
            mindSystem.TransferTo(mind, trackedCrew, mind: mind);
            playerManager.SetAttachedEntity(serverSession, trackedCrew);

            Assert.That(stationJobs.TryAssignJob(station, "TCaptain", trackedUser), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            entityManager.DeleteEntity(trackedCrew);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(stationJobs.IsPlayerJobTracked(station, trackedUser, "TCaptain"), Is.False);
            Assert.That(stationJobs.TryGetJobSlot(station, "TCaptain", out var slots), Is.True);
            Assert.That(slots, Is.EqualTo(5));
        });

        await pair.CleanReturnAsync();
    }

    private static TickerJobsAvailableEvent InvokeGenerateJobsAvailableEvent(StationJobsSystem system)
    {
        var method = typeof(StationJobsSystem).GetMethod(
            "GenerateJobsAvailableEvent",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        return (TickerJobsAvailableEvent) method!.Invoke(system, Array.Empty<object>())!;
    }

    private static HashSet<EntityUid> InvokeGetStationsWithCrewRecordsConsole(StationJobsSystem system)
    {
        var method = typeof(StationJobsSystem).GetMethod(
            "GetStationsWithCrewRecordsConsole",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        return (HashSet<EntityUid>) method!.Invoke(system, Array.Empty<object>())!;
    }

    private static bool InvokeShouldReopenTrackedJob(JobTrackingSystem system, EntityUid station, string jobId)
    {
        var method = typeof(JobTrackingSystem).GetMethod(
            "ShouldReopenTrackedJob",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        return (bool) method!.Invoke(system, new object[] { station, (ProtoId<JobPrototype>) jobId })!;
    }

    private static void SeedColcommRegistry(ColcommJobRegistryComponent registry, int mercenarySlots = 2)
    {
        SetRegistryField(registry, "ConfiguredJobs", new Dictionary<ProtoId<JobPrototype>, int[]>
        {
            ["Mercenary"] = [0, mercenarySlots],
        });

        SetRegistryField(registry, "CurrentSlots", new Dictionary<ProtoId<JobPrototype>, int?>
        {
            ["Mercenary"] = mercenarySlots,
        });

        SetRegistryField(registry, "MidRoundMaxSlots", new Dictionary<ProtoId<JobPrototype>, int>
        {
            ["Mercenary"] = mercenarySlots,
        });
    }

    private static void SetRegistryField<TValue>(ColcommJobRegistryComponent registry, string fieldName, TValue value)
    {
        var field = typeof(ColcommJobRegistryComponent).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(registry, value);
    }

    private static void ForceGridUid(TransformComponent transform, EntityUid gridUid)
    {
        var field = typeof(TransformComponent).GetField("_gridUid", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(transform, gridUid);
    }
}

internal static class JobExtensions
{
    public static Dictionary<NetUserId, HumanoidCharacterProfile> AddJob(
        this Dictionary<NetUserId, HumanoidCharacterProfile> inp, string jobId, JobPriority prio = JobPriority.Medium,
        int amount = 1)
    {
        for (var i = 0; i < amount; i++)
        {
            inp.Add(new NetUserId(Guid.NewGuid()), HumanoidCharacterProfile.Random().WithJobPriority(jobId, prio));
        }

        return inp;
    }

    public static Dictionary<NetUserId, HumanoidCharacterProfile> AddPreference(
        this Dictionary<NetUserId, HumanoidCharacterProfile> inp, string jobId, JobPriority prio = JobPriority.Medium)
    {
        return inp.ToDictionary(x => x.Key, x => x.Value.WithJobPriority(jobId, prio));
    }

    public static Dictionary<NetUserId, HumanoidCharacterProfile> WithPlayers(
        this Dictionary<NetUserId, HumanoidCharacterProfile> inp,
        Dictionary<NetUserId, HumanoidCharacterProfile> second)
    {
        return new[] { inp, second }.SelectMany(x => x).ToDictionary(x => x.Key, x => x.Value);
    }
}
