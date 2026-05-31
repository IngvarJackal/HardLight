using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.IntegrationTests.Tests.Weapons;

public sealed class StackedAmmoReloadTests : InteractionTest
{
    [Test]
    public async Task ShotgunHandfulLoadsOneShellAtATime()
    {
        await SpawnTarget("WeaponShotgunM42A2");
        var shells = await PlaceInHands("CMShellShotgunBuckshot", 5);

        await Interact();

        var gun = STarget!.Value;
        var shellEntity = SEntMan.GetEntity(shells);
        var ballistic = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun);
        var stack = SEntMan.GetComponent<StackComponent>(shellEntity);

        Assert.Multiple(() =>
        {
            Assert.That(Hands.ActiveHandEntity, Is.EqualTo(shellEntity));
            Assert.That(stack.Count, Is.EqualTo(4));
            Assert.That(ballistic.UnspawnedCount, Is.Zero);
            Assert.That(ballistic.Container.ContainedEntities.Count, Is.EqualTo(1));
            Assert.That(ballistic.Container.ContainedEntities.Single(), Is.Not.EqualTo(shellEntity));
        });
    }
}