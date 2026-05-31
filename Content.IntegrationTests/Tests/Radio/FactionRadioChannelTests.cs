using System.Linq;
using System.Numerics;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Mono.Company;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Radio;

[TestFixture]
public sealed class FactionRadioChannelTests
{
    [Test]
    public async Task FactionChannelOnlyDeliversToCurrentCompany()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapLoader = server.System<MapLoaderSystem>();
        var radioSystem = server.System<RadioSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(new ResPath("/Maps/Test/empty.yml"), out _, out var grids), Is.True);
            var grid = grids.Single();

            var source = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var alliedListener = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var outsiderListener = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));

            SetCompany(entMan, source, "Arcadia");
            SetCompany(entMan, alliedListener, "Arcadia");
            SetCompany(entMan, outsiderListener, "InterdynePharmaceuticals");

            var alliedRadio = SpawnReceiverRadio(entMan, alliedListener, "Faction", "Mothership");
            var outsiderRadio = SpawnReceiverRadio(entMan, outsiderListener, "Faction", "Mothership");

            radioSystem.SendRadioMessage(source, "current faction only", "Faction", source);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<RadioReceiveCounterComponent>(alliedRadio).ReceiveCount, Is.EqualTo(1));
                Assert.That(entMan.GetComponent<RadioReceiveCounterComponent>(outsiderRadio).ReceiveCount, Is.EqualTo(0));
            });

            entMan.GetComponent<RadioReceiveCounterComponent>(alliedRadio).ReceiveCount = 0;
            entMan.GetComponent<RadioReceiveCounterComponent>(outsiderRadio).ReceiveCount = 0;

            radioSystem.SendRadioMessage(source, "unrestricted long range", "Mothership", source);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<RadioReceiveCounterComponent>(alliedRadio).ReceiveCount, Is.EqualTo(1));
                Assert.That(entMan.GetComponent<RadioReceiveCounterComponent>(outsiderRadio).ReceiveCount, Is.EqualTo(1));
            });
        });

        await pair.CleanReturnAsync();
    }

    private static void SetCompany(IEntityManager entMan, EntityUid uid, string companyName)
    {
        var company = entMan.EnsureComponent<CompanyComponent>(uid);
        company.CompanyName = companyName;
    }

    private static EntityUid SpawnReceiverRadio(IEntityManager entMan, EntityUid listener, params string[] channels)
    {
        var radio = entMan.SpawnEntity(null, new EntityCoordinates(listener, Vector2.Zero));
        var activeRadio = entMan.EnsureComponent<ActiveRadioComponent>(radio);
        entMan.EnsureComponent<RadioReceiveCounterComponent>(radio);

        foreach (var channel in channels)
        {
            activeRadio.Channels.Add(channel);
        }

        return radio;
    }
}

[RegisterComponent]
public sealed partial class RadioReceiveCounterComponent : Component
{
    public int ReceiveCount;
}

public sealed class RadioReceiveCounterSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<RadioReceiveCounterComponent, RadioReceiveEvent>(OnReceive);
    }

    private void OnReceive(Entity<RadioReceiveCounterComponent> ent, ref RadioReceiveEvent args)
    {
        ent.Comp.ReceiveCount++;
    }
}