#nullable enable
using System.Linq;
using Castle.Components.DictionaryAdapter.Xml;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests
{
    [TestFixture]
    public sealed class DummyIconTest
    {
        [Test]
        public async Task Test()
        {
            await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
            var client = pair.Client;
            var prototypeManager = client.ResolveDependency<IPrototypeManager>();
            var resourceCache = client.ResolveDependency<IResourceCache>();
            var spriteSys = client.System<SpriteSystem>(); // HL: Move to the proper usage of the SpriteSystem for getting sprites

            await client.WaitAssertion(() =>
            {
                foreach (var proto in prototypeManager.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.HideSpawnMenu || proto.Abstract || pair.IsTestPrototype(proto) || !proto.Components.ContainsKey("Sprite"))
                        continue;

                    Assert.DoesNotThrow(() =>
                    {
                        var _ = spriteSys.GetPrototypeTextures(proto).ToList(); // HL: Move to the proper usage of the SpriteSystem for getting sprites
                    }, "Prototype {0} threw an exception when getting its textures.",
                        proto.ID);
                }
            });
            await pair.CleanReturnAsync();
        }
    }
}
