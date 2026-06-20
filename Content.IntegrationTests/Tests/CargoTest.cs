using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Cargo.Components; // Frontier
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class CargoTest
{
    private static readonly HashSet<ProtoId<CargoProductPrototype>> Ignored =
    [
        // This is ignored because it is explicitly intended to be able to sell for more than it costs.
        new("FunCrateGambling")
    ];

    [Test]
    public async Task NoCargoOrderArbitrage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var pricing = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<PricingSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in protoManager.EnumeratePrototypes<CargoProductPrototype>())
                {
                    if (Ignored.Contains(proto.ID))
                        continue;

                    var ent = entManager.SpawnEntity(proto.Product, testMap.MapCoords);
                    var price = pricing.GetPrice(ent);

                    Assert.That(price, Is.AtMost(proto.Cost), $"Found arbitrage on {proto.ID} cargo product! Cost is {proto.Cost} but sell is {price}!");
                    entManager.DeleteEntity(ent);
                }
            });
        });

        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task NoCargoBountyArbitrageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var cargo = entManager.System<CargoSystem>();

        var bounties = protoManager.EnumeratePrototypes<CargoBountyPrototype>().ToList();

        await server.WaitAssertion(() =>
        {
            var mapId = testMap.MapId;

            Assert.Multiple(() =>
            {
                foreach (var proto in protoManager.EnumeratePrototypes<CargoProductPrototype>())
                {
                    var ent = entManager.SpawnEntity(proto.Product, new MapCoordinates(Vector2.Zero, mapId));

                    foreach (var bounty in bounties)
                    {
                        if (cargo.IsBountyComplete(ent, bounty))
                            Assert.That(proto.Cost, Is.GreaterThanOrEqualTo(bounty.Reward), $"Found arbitrage on {bounty.ID} cargo bounty! Product {proto.ID} costs {proto.Cost} but fulfills bounty {bounty.ID} with reward {bounty.Reward}!");
                    }

                    entManager.DeleteEntity(ent);
                }
            });

            mapSystem.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NoStaticPriceAndStackPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var protoManager = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var protoIds = protoManager.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract)
                .Where(p => !pair.IsTestPrototype(p))
                .Where(p => p.Components.ContainsKey("StaticPrice"))
                .ToList();

            foreach (var proto in protoIds)
            {
                // Sanity check
                Assert.That(proto.TryGetComponent<StaticPriceComponent>(out var staticPriceComp, compFact), Is.True);

                if (proto.TryGetComponent<StackPriceComponent>(out var stackPriceComp, compFact) && stackPriceComp.Price > 0)
                {
                    Assert.That(staticPriceComp.Price, Is.EqualTo(0),
                        $"The prototype {proto} has a StackPriceComponent and StaticPriceComponent whose values are not compatible with each other.");
                }

                if (proto.HasComponent<StackComponent>(compFact))
                {
                    Assert.That(staticPriceComp.Price, Is.EqualTo(0),
                        $"The prototype {proto} has a StackComponent and StaticPriceComponent whose values are not compatible with each other.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests to see if any items that are valid for cargo bounties can be sliced into items that
    /// are also valid for the same bounty entry.
    /// </summary>
    [Test]
    public async Task NoSliceableBountyArbitrageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var whitelist = entManager.System<EntityWhitelistSystem>();
        var cargo = entManager.System<CargoSystem>();
        var sliceableSys = entManager.System<SliceableFoodSystem>();

        var bounties = protoManager.EnumeratePrototypes<CargoBountyPrototype>().ToList();

        await server.WaitAssertion(() =>
        {
            var mapId = testMap.MapId;
            var grid = mapManager.CreateGridEntity(mapId);
            var coord = new EntityCoordinates(grid.Owner, 0, 0);

            var sliceableEntityProtos = protoManager.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract)
                .Where(p => !pair.IsTestPrototype(p))
                .Where(p => p.TryGetComponent<SliceableFoodComponent>(out _, componentFactory))
                .Select(p => p.ID)
                .ToList();

            foreach (var proto in sliceableEntityProtos)
            {
                var ent = entManager.SpawnEntity(proto, coord);
                var sliceable = entManager.GetComponent<SliceableFoodComponent>(ent);

                // Check each bounty
                foreach (var bounty in bounties)
                {
                    // Check each entry in the bounty
                    foreach (var entry in bounty.Entries)
                    {
                        // See if the entity counts as part of this bounty entry
                        if (!cargo.IsValidBountyEntry(ent, entry))
                            continue;

                        // Spawn a slice
                        var slice = entManager.SpawnEntity(sliceable.Slice, coord);

                        // See if the slice also counts for this bounty entry
                        if (!cargo.IsValidBountyEntry(slice, entry))
                        {
                            entManager.DeleteEntity(slice);
                            continue;
                        }

                        entManager.DeleteEntity(slice);

                        // If for some reason it can only make one slice, that's okay, I guess
                        Assert.That(sliceable.TotalCount, Is.EqualTo(1), $"{proto} counts as part of cargo bounty {bounty.ID} and slices into {sliceable.TotalCount} slices which count for the same bounty!");
                    }
                }

                entManager.DeleteEntity(ent);
            }
            mapSystem.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }

    [TestPrototypes]
    private const string StackProto = @"
- type: entity
  id: A

- type: stack
  id: StackProto
  name: stack-steel
  spawn: A

- type: entity
  id: StackEnt
  components:
  - type: StackPrice
    price: 20
  - type: Stack
    stackType: StackProto
    count: 5
";

    [TestPrototypes]
    private const string SpawnItemsOnUsePricingProto =
        "- type: entity\n" +
        "  id: SpawnItemsOnUsePricingSpawned\n" +
        "  components:\n" +
        "  - type: StaticPrice\n" +
        "    price: 25\n" +
        "  - type: TestPriceInitSideEffect\n" +
        "\n" +
        "- type: entity\n" +
        "  id: SpawnItemsOnUsePricingSpawner\n" +
        "  components:\n" +
        "  - type: SpawnItemsOnUse\n" +
        "    items:\n" +
        "    - id: SpawnItemsOnUsePricingSpawned\n";

    [Test]
    public async Task StackPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var priceSystem = entManager.System<PricingSystem>();

            var ent = entManager.SpawnEntity("StackEnt", MapCoordinates.Nullspace);
            var price = priceSystem.GetPrice(ent);
            Assert.That(price, Is.EqualTo(100.0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurePriceAvoidsSpawnItemsOnUseInstantiation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var priceSystem = entManager.System<PricingSystem>();
            var initSystem = entManager.System<TestPriceInitSideEffectSystem>();

            var ent = entManager.SpawnEntity("SpawnItemsOnUsePricingSpawner", MapCoordinates.Nullspace);

            initSystem.InitCount = 0;
            var purePrice = priceSystem.GetPrice(ent, allowSideEffects: false);
            Assert.Multiple(() =>
            {
                Assert.That(purePrice, Is.EqualTo(25.0));
                Assert.That(initSystem.InitCount, Is.EqualTo(0));
            });

            var normalPrice = priceSystem.GetPrice(ent);
            Assert.Multiple(() =>
            {
                Assert.That(normalPrice, Is.EqualTo(25.0));
                Assert.That(initSystem.InitCount, Is.EqualTo(1));
            });

            entManager.DeleteEntity(ent);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GridAppraisalRespectsContentsAndTopLevelPredicate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var conSystem = entManager.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var priceSystem = entManager.System<PricingSystem>();

            var containerEntity = entManager.SpawnEntity(null, testMap.GridCoords);
            var container = conSystem.EnsureContainer<Container>(containerEntity, "GridAppraisalThresholdContainer");

            var valuable = entManager.SpawnEntity(null, testMap.GridCoords);
            entManager.AddComponent<StaticPriceComponent>(valuable).Price = 80;

            var filler = entManager.SpawnEntity(null, testMap.GridCoords);
            entManager.AddComponent<StaticPriceComponent>(filler).Price = 30;

            var ignored = entManager.SpawnEntity(null, testMap.GridCoords);
            entManager.AddComponent<StaticPriceComponent>(ignored).Price = 50;

            Assert.That(conSystem.Insert(valuable, container), Is.True);
            Assert.That(conSystem.Insert(filler, container), Is.True);

            var containerPrice = priceSystem.GetPrice(containerEntity);
            var total = priceSystem.AppraiseGrid(testMap.Grid);
            Assert.Multiple(() =>
            {
                Assert.That(containerPrice, Is.EqualTo(110.0));
                Assert.That(total, Is.EqualTo(160.0));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 79), Is.EqualTo(total > 79));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 109), Is.EqualTo(total > 109));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 160), Is.EqualTo(total > 160));
            });

            bool FilterIgnored(EntityUid uid)
            {
                return uid != ignored;
            }

            var visited = new List<EntityUid>();
            var filteredIgnoredPrice = priceSystem.GetPrice(ignored, true, FilterIgnored);
            var filteredTotal = priceSystem.AppraiseGrid(testMap.Grid, FilterIgnored, (uid, _) => visited.Add(uid));
            Assert.Multiple(() =>
            {
                Assert.That(filteredIgnoredPrice, Is.EqualTo(0.0));
                Assert.That(filteredTotal, Is.EqualTo(110.0));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 79, FilterIgnored), Is.EqualTo(filteredTotal > 79));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 109, FilterIgnored), Is.EqualTo(filteredTotal > 109));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 110, FilterIgnored), Is.EqualTo(filteredTotal > 110));
                Assert.That(visited, Has.Count.EqualTo(1));
                Assert.That(visited, Has.Member(containerEntity));
                Assert.That(visited, Has.No.Member(ignored));
            });

            mapSystem.DeleteMap(testMap.MapId);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GridAppraisalExceedsSuppressesPriceSideEffects()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var conSystem = entManager.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var priceSystem = entManager.System<PricingSystem>();
            var sideEffectSystem = entManager.System<TestPriceSideEffectSystem>();

            var composite = entManager.SpawnEntity(null, testMap.GridCoords);
            entManager.AddComponent<TestPriceSideEffectComponent>(composite).Price = 100;
            var container = conSystem.EnsureContainer<Container>(composite, "GridAppraisalSideEffectContainer");

            var negative = entManager.SpawnEntity(null, testMap.GridCoords);
            entManager.AddComponent<TestPriceSideEffectComponent>(negative).Price = -90;
            Assert.That(conSystem.Insert(negative, container), Is.True);

            var positive = entManager.SpawnEntity(null, testMap.GridCoords);
            entManager.AddComponent<TestPriceSideEffectComponent>(positive).Price = 40;

            sideEffectSystem.SideEffectCount = 0;

            var compositePrice = priceSystem.GetPrice(composite);
            var total = priceSystem.AppraiseGrid(testMap.Grid);
            Assert.Multiple(() =>
            {
                Assert.That(compositePrice, Is.EqualTo(10.0));
                Assert.That(total, Is.EqualTo(50.0));
                Assert.That(sideEffectSystem.SideEffectCount, Is.EqualTo(5));
            });

            sideEffectSystem.SideEffectCount = 0;
            Assert.Multiple(() =>
            {
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 49), Is.EqualTo(total > 49));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 50), Is.EqualTo(total > 50));
                Assert.That(priceSystem.AppraiseGridExceeds(testMap.Grid, 60), Is.EqualTo(total > 60));
                Assert.That(sideEffectSystem.SideEffectCount, Is.EqualTo(0));
            });

            mapSystem.DeleteMap(testMap.MapId);
        });

        await pair.CleanReturnAsync();
    }
}

[RegisterComponent]
public sealed partial class TestPriceSideEffectComponent : Component
{
    public double Price;
}

[RegisterComponent]
public sealed partial class TestPriceInitSideEffectComponent : Component;

public sealed class TestPriceSideEffectSystem : EntitySystem
{
    public int SideEffectCount;

    public override void Initialize()
    {
        SubscribeLocalEvent<TestPriceSideEffectComponent, PriceCalculationEvent>(OnPriceCalculation);
    }

    private void OnPriceCalculation(Entity<TestPriceSideEffectComponent> ent, ref PriceCalculationEvent args)
    {
        args.Price += ent.Comp.Price;

        if (args.AllowSideEffects)
            SideEffectCount++;
    }
}

public sealed class TestPriceInitSideEffectSystem : EntitySystem
{
    public int InitCount;

    public override void Initialize()
    {
        SubscribeLocalEvent<TestPriceInitSideEffectComponent, ComponentInit>(OnComponentInit);
    }

    private void OnComponentInit(Entity<TestPriceInitSideEffectComponent> ent, ref ComponentInit args)
    {
        InitCount++;
    }
}
