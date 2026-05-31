using Content.Shared._HL.Mobs.Components;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._HL.Mobs.Systems;

/*
 * Vox Froguli - This is where the magboot-esque magic happens
*/
public sealed class VoxTalonsSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WhitelistVoxTalonsTag = "WhitelistVoxTalons";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VoxTalonsComponent, IsWeightlessEvent>(OnIsWeightless);
        SubscribeLocalEvent<VoxTalonsComponent, InventoryRelayedEvent<IsWeightlessEvent>>(OnIsWeightless);
    }
    private void OnIsWeightless(Entity<VoxTalonsComponent> ent, ref IsWeightlessEvent args)
    {
        if (args.Handled || !_toggle.IsActivated(ent.Owner))
            return;

        // do not cancel weightlessness if the person is in off-grid.
        if (!_gravity.EntityOnGravitySupportingGridOrMap(ent.Owner))
            return;
        // do not cancel weightlessness if the person is wearing shoes that don't have the correct tag
        if (_inventory.TryGetSlotEntity(ent, "shoes", out var worn) && worn != null && !_tag.HasTag((EntityUid)worn, WhitelistVoxTalonsTag))
            return;

        args.IsWeightless = false;
        args.Handled = true;
    }

    private void OnIsWeightless(Entity<VoxTalonsComponent> ent, ref InventoryRelayedEvent<IsWeightlessEvent> args)
    {
        OnIsWeightless(ent, ref args.Args);
    }
}
