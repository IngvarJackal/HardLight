using System.Linq;
using System.Numerics;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Weapons;

public sealed class BallisticAmmoSaveLoadTests
{
    [TestCase(0)]
    [TestCase(3)]
    public async Task MagazineCountPersistsAcrossGridSaveLoad(int remainingRounds)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var gunSystem = server.System<GunSystem>();
        var savePath = new ResPath($"/magazine-save-load-{remainingRounds}.yml");

        await server.WaitPost(() =>
        {
            Assert.That(mapLoader.TryLoadMap(new ResPath("/Maps/Test/empty.yml"), out _, out var grids), Is.True);
            var sourceGrid = grids.Single();

            var magazine = entManager.SpawnEntity("MagazinePistol", new EntityCoordinates(sourceGrid, new Vector2(0.5f, 0.5f)));
            var ballistic = entManager.GetComponent<BallisticAmmoProviderComponent>(magazine);

            Assert.That(ballistic.FillFromPrototype, Is.False);

            gunSystem.SetBallisticUnspawned((magazine, ballistic), remainingRounds);

            Assert.That(ballistic.Count, Is.EqualTo(remainingRounds));
            Assert.That(mapLoader.TrySaveGrid(sourceGrid, savePath), Is.True);

            mapSystem.CreateMap(out var loadMapId);
            Assert.That(mapLoader.TryLoadGrid(loadMapId, savePath, out var loadedGrid), Is.True);
            Assert.That(loadedGrid, Is.Not.Null);

            EntityUid? loadedMagazine = null;
            BallisticAmmoProviderComponent loadedBallistic = null!;
            var query = entManager.EntityQueryEnumerator<BallisticAmmoProviderComponent, TransformComponent>();

            while (query.MoveNext(out var uid, out var provider, out var xform))
            {
                if (xform.GridUid != loadedGrid.Value)
                    continue;

                loadedMagazine = uid;
                loadedBallistic = provider;
                break;
            }

            Assert.That(loadedMagazine, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(loadedBallistic.Count, Is.EqualTo(remainingRounds));
                Assert.That(loadedBallistic.UnspawnedCount, Is.EqualTo(remainingRounds));
                Assert.That(loadedBallistic.FillFromPrototype, Is.False);
            });
        });

        await server.WaitIdleAsync();

        await pair.CleanReturnAsync();
    }
}