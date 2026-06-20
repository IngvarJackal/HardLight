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
    [TestPrototypes] // HL: Added custom weapon prototype to deal with fillFromPrototype defaulting to True now.
    private const string Prototypes = @"
- type: entity
  categories: [ HideSpawnMenu ]
  id: MagazinePistolTest
  name: pistol magazine (.35 auto)
  parent: BaseMagazinePistol
  description: 10-round single-stack magazine for pistols for testing!
  components:
  - type: BallisticAmmoProvider
    proto: CartridgePistol # Frontier
    fillFromPrototype: false
  - type: Sprite
    layers:
    - state: red
      map: [""enum.GunVisualLayers.Base""]
    - state: mag-1
      map: [""enum.GunVisualLayers.Mag""]
";
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
        var testMap = await pair.CreateTestMap(); // HL: Changed to TestMap, debug.yml was erroring for reasons

        await server.WaitPost(() =>
        {
            var magazine = entManager.SpawnEntity("MagazinePistolTest", new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            var ballistic = entManager.GetComponent<BallisticAmmoProviderComponent>(magazine);

            Assert.That(ballistic.FillFromPrototype, Is.False);

            gunSystem.SetBallisticUnspawned((magazine, ballistic), remainingRounds);

            Assert.That(ballistic.Count, Is.EqualTo(remainingRounds));
            Assert.That(mapLoader.TrySaveGrid(testMap.Grid, savePath), Is.True);

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
