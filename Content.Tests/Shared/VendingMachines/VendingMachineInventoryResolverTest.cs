using System;
using Content.Shared.VendingMachines;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Tests.Shared.VendingMachines;

[TestFixture]
[TestOf(typeof(VendingMachineInventoryResolver))]
public sealed class VendingMachineInventoryResolverTest : ContentUnitTest
{
    private IPrototypeManager _prototypeManager = default!;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _prototypeManager.Initialize();
        _prototypeManager.LoadString(TestPrototypes);
        _prototypeManager.ResolveResults();
    }

    private const string TestPrototypes = @"
- type: entity
  id: TestVendItemA

- type: entity
  id: TestVendItemB

- type: entity
  id: TestVendItemC

- type: vendingMachineInventory
  id: ResolverTestBase
  startingInventory:
    TestVendItemA: 3
    TestVendItemB: 5

- type: vendingMachineInventory
  id: ResolverTestChildOverride
  inherits: ResolverTestBase
  startingInventory:
    TestVendItemB: 10
    TestVendItemC: 1

- type: vendingMachineInventory
  id: ResolverTestUnlimitedChild
  inherits: ResolverTestBase
  inheritAsUnlimited: true
  startingInventory:
    TestVendItemC: 2

- type: vendingMachineInventory
  id: ResolverTestCycleA
  inherits: ResolverTestCycleB
  startingInventory:
    TestVendItemA: 1

- type: vendingMachineInventory
  id: ResolverTestCycleB
  inherits: ResolverTestCycleA
  startingInventory:
    TestVendItemB: 1
";

    [Test]
    public void NoInheritance_ReturnsOwnEntries()
    {
        var proto = _prototypeManager.Index<VendingMachineInventoryPrototype>("ResolverTestBase");
        var result = VendingMachineInventoryResolver.ResolveRegular(_prototypeManager, proto);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result["TestVendItemA"], Is.EqualTo((uint)3));
        Assert.That(result["TestVendItemB"], Is.EqualTo((uint)5));
    }

    [Test]
    public void Inheritance_ChildOverridesParent()
    {
        var proto = _prototypeManager.Index<VendingMachineInventoryPrototype>("ResolverTestChildOverride");
        var result = VendingMachineInventoryResolver.ResolveRegular(_prototypeManager, proto);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result["TestVendItemA"], Is.EqualTo((uint)3), "Inherited unmodified key should pass through.");
        Assert.That(result["TestVendItemB"], Is.EqualTo((uint)10), "Child should override parent value.");
        Assert.That(result["TestVendItemC"], Is.EqualTo((uint)1));
    }

    [Test]
    public void InheritAsUnlimited_ExpandsParentEntriesToMaxValue()
    {
        var proto = _prototypeManager.Index<VendingMachineInventoryPrototype>("ResolverTestUnlimitedChild");
        var result = VendingMachineInventoryResolver.ResolveRegular(_prototypeManager, proto);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result["TestVendItemA"], Is.EqualTo(uint.MaxValue));
        Assert.That(result["TestVendItemB"], Is.EqualTo(uint.MaxValue));
        Assert.That(result["TestVendItemC"], Is.EqualTo((uint)2), "Local entries are not affected by inheritAsUnlimited.");
    }

    [Test]
    public void CircularInheritance_Throws()
    {
        var proto = _prototypeManager.Index<VendingMachineInventoryPrototype>("ResolverTestCycleA");

        Assert.Throws<InvalidOperationException>(
            () => VendingMachineInventoryResolver.ResolveRegular(_prototypeManager, proto));
    }
}
