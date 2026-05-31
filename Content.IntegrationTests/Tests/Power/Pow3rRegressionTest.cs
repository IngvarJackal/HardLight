#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Power;

/// <summary>
/// Regression harness for the Pow3r power solver and the per-tick PowerNet
/// update path.
///
/// Phase 0 of the multi-PR PowerNet/Pow3r optimization plan (see POWER_PLAN/).
/// This test exists to gate every subsequent perf PR: any change to solver
/// behavior must either keep the snapshots within epsilon, or be accompanied
/// by a deliberate update to the expected values with a one-line justification.
///
/// Design choice: Rather than golden JSON files (which create chicken-and-egg
/// problems in CI), this test asserts:
///   1. Steady-state values match analytic expectations (e.g. balanced supply
///      and demand produce exact ReceivingPower == DrawRate).
///   2. The solver is *stable* — values at tick 60 match values at tick 600
///      within tight epsilon (i.e. no slow drift).
///   3. Battery storage evolves monotonically as expected over the run.
///
/// Properties (1) and (2) catch any solver math change that shifts equilibrium
/// or introduces drift. Property (3) catches gross errors in battery accounting.
/// </summary>
[TestFixture]
public sealed class Pow3rRegressionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: Pow3rRegressionGenerator
  components:
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerSupplier
  - type: Transform
    anchored: true

- type: entity
  id: Pow3rRegressionConsumer
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerConsumer

- type: entity
  id: Pow3rRegressionDischargeBattery
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerNetworkBattery
    maxSupply: 500
    supplyRampTolerance: 500
  - type: Battery
    maxCharge: 10000
    startingCharge: 5000
  - type: BatteryDischarger
";

    /// <summary>
    /// Scenario: 1 generator with surplus capacity feeds 3 identical consumers
    /// on a single HV network. With supply &gt; demand, every consumer should
    /// receive exactly its draw rate, and the system should be stable across
    /// hundreds of ticks.
    /// </summary>
    [Test]
    public async Task SteadyStateSurplus_StableAcrossTicks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var mapManager = server.ResolveDependency<IMapManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var mapSys = entityManager.System<SharedMapSystem>();

        const float drawRate = 150f;
        const int consumerCount = 3;

        PowerSupplierComponent supplier = default!;
        var consumers = new List<PowerConsumerComponent>();

        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);

            // One row of cables, anchored.
            for (var i = 0; i < consumerCount + 1; i++)
            {
                mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                entityManager.SpawnEntity("CableHV", new EntityCoordinates(grid.Owner, 0, i));
            }

            var generatorEnt = entityManager.SpawnEntity(
                "Pow3rRegressionGenerator", new EntityCoordinates(grid.Owner, 0, 0));
            supplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
            // Comfortable surplus — supplyRatio should be exactly 1 every tick.
            supplier.MaxSupply = drawRate * consumerCount * 4;
            supplier.SupplyRampTolerance = drawRate * consumerCount * 4;

            for (var i = 0; i < consumerCount; i++)
            {
                var consumerEnt = entityManager.SpawnEntity(
                    "Pow3rRegressionConsumer", new EntityCoordinates(grid.Owner, 0, i + 1));
                var consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);
                consumer.DrawRate = drawRate;
                consumers.Add(consumer);
            }
        });

        // Snapshot at three points to catch slow drift.
        var snap1 = await SnapshotAfterTicks(server, supplier, consumers, ticks: 1);
        var snap60 = await SnapshotAfterTicks(server, supplier, consumers, ticks: 59);
        var snap600 = await SnapshotAfterTicks(server, supplier, consumers, ticks: 540);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // Property 1: equilibrium matches analytic value.
                for (var i = 0; i < consumerCount; i++)
                {
                    Assert.That(snap60.ConsumerReceived[i], Is.EqualTo(drawRate).Within(0.1),
                        $"Consumer {i} received power at tick 60 should equal draw rate.");
                }
                Assert.That(snap60.SupplierCurrent, Is.EqualTo(drawRate * consumerCount).Within(0.1),
                    "Supplier output at tick 60 should equal total draw.");

                // Property 2: stability — no drift between tick 60 and tick 600.
                for (var i = 0; i < consumerCount; i++)
                {
                    Assert.That(snap600.ConsumerReceived[i], Is.EqualTo(snap60.ConsumerReceived[i]).Within(1e-4),
                        $"Consumer {i} drifted between tick 60 and tick 600.");
                }
                Assert.That(snap600.SupplierCurrent, Is.EqualTo(snap60.SupplierCurrent).Within(1e-4),
                    "Supplier drifted between tick 60 and tick 600.");

                // Sanity: tick 1 may not have ramped fully, but should not exceed equilibrium.
                Assert.That(snap1.SupplierCurrent, Is.LessThanOrEqualTo(snap60.SupplierCurrent + 0.1),
                    "Supplier overshoots equilibrium on first tick.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Scenario: under-supply. With supply &lt; demand, consumers receive a
    /// proportional share. Tests that the supplyRatio path is exercised and
    /// stable.
    /// </summary>
    [Test]
    public async Task SteadyStateDeficit_StableAcrossTicks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var mapManager = server.ResolveDependency<IMapManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var mapSys = entityManager.System<SharedMapSystem>();

        const float drawRate = 200f;
        const int consumerCount = 3;
        const float supplyCap = drawRate * 1.5f; // 50% of total demand.

        PowerSupplierComponent supplier = default!;
        var consumers = new List<PowerConsumerComponent>();

        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);

            for (var i = 0; i < consumerCount + 1; i++)
            {
                mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                entityManager.SpawnEntity("CableHV", new EntityCoordinates(grid.Owner, 0, i));
            }

            var generatorEnt = entityManager.SpawnEntity(
                "Pow3rRegressionGenerator", new EntityCoordinates(grid.Owner, 0, 0));
            supplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
            supplier.MaxSupply = supplyCap;
            supplier.SupplyRampTolerance = supplyCap;

            for (var i = 0; i < consumerCount; i++)
            {
                var consumerEnt = entityManager.SpawnEntity(
                    "Pow3rRegressionConsumer", new EntityCoordinates(grid.Owner, 0, i + 1));
                var consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);
                consumer.DrawRate = drawRate;
                consumers.Add(consumer);
            }
        });

        var snap60 = await SnapshotAfterTicks(server, supplier, consumers, ticks: 60);
        var snap600 = await SnapshotAfterTicks(server, supplier, consumers, ticks: 540);

        var totalDemand = drawRate * consumerCount;
        var expectedRatio = supplyCap / totalDemand;
        var expectedReceived = drawRate * expectedRatio;

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                for (var i = 0; i < consumerCount; i++)
                {
                    Assert.That(snap60.ConsumerReceived[i], Is.EqualTo(expectedReceived).Within(0.1),
                        $"Consumer {i} should receive proportional share at tick 60.");
                    Assert.That(snap600.ConsumerReceived[i], Is.EqualTo(snap60.ConsumerReceived[i]).Within(1e-4),
                        $"Consumer {i} drifted between tick 60 and tick 600.");
                }
                Assert.That(snap60.SupplierCurrent, Is.EqualTo(supplyCap).Within(0.1),
                    "Supplier should run at maximum during deficit.");
                Assert.That(snap600.SupplierCurrent, Is.EqualTo(snap60.SupplierCurrent).Within(1e-4),
                    "Supplier output drifted across long run.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Scenario: discharging battery sourced load. With a battery as the only
    /// supplier and a single consumer drawing within the battery's capacity,
    /// charge should decrease monotonically and at the analytically-expected
    /// rate.
    /// </summary>
    [Test]
    public async Task BatteryDischarge_MonotonicDrain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var mapManager = server.ResolveDependency<IMapManager>();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var mapSys = entityManager.System<SharedMapSystem>();
        var gameTiming = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        const float drawRate = 100f;

        PowerConsumerComponent consumer = default!;
        BatteryComponent battery = default!;

        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);

            for (var i = 0; i < 2; i++)
            {
                mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                entityManager.SpawnEntity("CableHV", new EntityCoordinates(grid.Owner, 0, i));
            }

            var batteryEnt = entityManager.SpawnEntity(
                "Pow3rRegressionDischargeBattery", new EntityCoordinates(grid.Owner, 0, 0));
            battery = entityManager.GetComponent<BatteryComponent>(batteryEnt);

            var consumerEnt = entityManager.SpawnEntity(
                "Pow3rRegressionConsumer", new EntityCoordinates(grid.Owner, 0, 1));
            consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);
            consumer.DrawRate = drawRate;
        });

        // Run a brief warm-up so the supply ramp settles.
        server.RunTicks(30);

        float chargeAfterWarmup = 0f;
        await server.WaitAssertion(() =>
        {
            chargeAfterWarmup = battery.CurrentCharge;
        });

        // Run another N ticks and verify monotonic decrease at the expected rate.
        const int testTicks = 60;
        server.RunTicks(testTicks);

        await server.WaitAssertion(() =>
        {
            var tickPeriod = (float)gameTiming.TickPeriod.TotalSeconds;
            var expectedDelta = drawRate * tickPeriod * testTicks;
            var actualDelta = chargeAfterWarmup - battery.CurrentCharge;

            Assert.Multiple(() =>
            {
                Assert.That(battery.CurrentCharge, Is.LessThan(chargeAfterWarmup),
                    "Battery charge should decrease over time when discharging.");
                // Allow 5% tolerance on drain rate to account for ramp behavior and
                // floating-point accumulation across many ticks.
                Assert.That(actualDelta, Is.EqualTo(expectedDelta).Within(expectedDelta * 0.05),
                    $"Battery drained {actualDelta:F2} J over {testTicks} ticks, "
                    + $"expected ~{expectedDelta:F2} J at {drawRate} W draw.");
                Assert.That(consumer.ReceivedPower, Is.EqualTo(drawRate).Within(0.1),
                    "Consumer should receive its full draw rate while battery has charge.");
            });
        });

        await pair.CleanReturnAsync();
    }

    private readonly record struct PowerSnapshot(
        float SupplierCurrent,
        IReadOnlyList<float> ConsumerReceived);

    private static async Task<PowerSnapshot> SnapshotAfterTicks(
        RobustIntegrationTest.ServerIntegrationInstance server,
        PowerSupplierComponent supplier,
        IReadOnlyList<PowerConsumerComponent> consumers,
        int ticks)
    {
        server.RunTicks(ticks);
        var supplierCurrent = 0f;
        var received = new List<float>(consumers.Count);
        await server.WaitAssertion(() =>
        {
            supplierCurrent = supplier.CurrentSupply;
            received.AddRange(consumers.Select(c => c.ReceivedPower));
        });
        return new PowerSnapshot(supplierCurrent, received);
    }
}
