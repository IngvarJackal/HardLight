using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.IntegrationTests.Tests.Weapons;

public sealed class StackedAmmoReloadTests : InteractionTest
{
    [TestPrototypes] // HL: Added custom weapon prototype to deal with fillFromPrototype defaulting to True now.
    private const string Prototypes = @"
- type: entity
  parent: [ RMCBaseWeaponShotgun, RMCBaseAttachableHolder ]
  name: M42A2 Pump Shotgun
  id: WeaponShotgunM42A2Test
  description: An Aegis Battlefield Armaments classic design, the M42A2 combines close-range firepower with long term reliability. Needs to be pumped.
  components:
  - type: Sprite
    sprite: _RMC14/Objects/Weapons/Guns/Shotguns/m42a2/desert.rsi
    layers:
    - state: icon
      map: [ ""enum.GunVisualLayers.Base"" ]
  - type: Clothing
    sprite: _RMC14/Objects/Weapons/Guns/Shotguns/m42a2/desert.rsi
  - type: BallisticAmmoProvider
    cycleable: false
    fillFromPrototype: false
    whitelist:
      tags:
      - RMCShellShotgun
  - type: Gun
    shotsPerBurst: 1
";
    [Test]
    public async Task ShotgunHandfulLoadsOneShellAtATime()
    {
        await SpawnTarget("WeaponShotgunM42A2Test");
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
