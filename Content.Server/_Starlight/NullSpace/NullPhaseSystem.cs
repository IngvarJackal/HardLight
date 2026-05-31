using Content.Shared.Inventory.Events;
using Content.Shared.Clothing.Components;
using Content.Shared.Actions;
using Content.Shared._Starlight.NullSpace;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Content.Shared.Popups;
using Content.Shared._Starlight;
using System.Linq;
using Content.Server.Ghost;
using Robust.Server.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Light.Components; // HardLight: Merged with upstream

namespace Content.Server._Starlight.NullSpace;

public sealed class EtherealPhaseSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly GhostSystem _ghost = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private EntProtoId ShadekinPhaseInEffect = "ShadekinPhaseInEffect";
    private EntProtoId ShadekinPhaseOutEffect = "ShadekinPhaseOutEffect";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NullPhaseComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NullPhaseComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NullPhaseComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<NullPhaseComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<NullPhaseComponent, NullPhaseActionEvent>(OnPhaseAction);
        SubscribeLocalEvent<NullPhaseComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<NullHarnessComponent, GotEquippedEvent>(OnHarnessEquipped);
        SubscribeLocalEvent<NullHarnessComponent, GotUnequippedEvent>(OnHarnessUnequipped);
    }

    private void OnStartup(EntityUid uid, NullPhaseComponent component, ComponentStartup args)
    {
        Toggle(uid, component, true);
    }

    public void OnShutdown(EntityUid uid, NullPhaseComponent component, ComponentShutdown args)
    {
        Toggle(uid, component, false);
    }

    private void OnEquipped(EntityUid uid, NullPhaseComponent component, GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing)
            || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        var playerComp = EnsureComp<NullPhaseComponent>(args.Equipee);
        playerComp.ExitDelay = component.ExitDelay;
        playerComp.ForcedEjectionPenalty = component.ForcedEjectionPenalty;
    }

    private void OnUnequipped(EntityUid uid, NullPhaseComponent component, GotUnequippedEvent args)
    {
        RemComp<NullPhaseComponent>(args.Equipee);
    }

    private void OnHarnessEquipped(EntityUid uid, NullHarnessComponent component, GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing) || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        EnsureComp<BlockNullPhaseComponent>(args.Equipee);
    }

    private void OnHarnessUnequipped(EntityUid uid, NullHarnessComponent component, GotUnequippedEvent args)
    {
        RemComp<BlockNullPhaseComponent>(args.Equipee);
    }

    private void OnUnequipAttempt(EntityUid uid, NullPhaseComponent component, ref BeingUnequippedAttemptEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing) || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        if (!TryComp<NullPhaseComponent>(args.UnEquipTarget, out var playerComp))
            return;

        if (!_actionsSystem.TryGetActionData(playerComp.PhaseAction, out var action, logError: false))
            return;

        if (action.Cooldown is { End: var end } && end > _gameTiming.CurTime
            && args.Unequipee == args.UnEquipTarget)
        {
            args.Cancel();
            _popup.PopupEntity(Loc.GetString("null-phase-cooldown-locked"), args.UnEquipTarget, args.UnEquipTarget);
        }
    }

    private void OnPhaseAction(EntityUid uid, NullPhaseComponent component, NullPhaseActionEvent args)
    {
        if (HasComp<BlockNullPhaseComponent>(args.Performer))
        {
            _popup.PopupEntity(Loc.GetString("null-harness-blocks-phase"), args.Performer, args.Performer);
            args.Handled = true;
            return;
        }

        // Apply exit cooldown when leaving; no cooldown on enter
        var exitingNullspace = HasComp<NullSpaceComponent>(args.Performer);
        _actionsSystem.SetUseDelay(component.PhaseAction, exitingNullspace ? TimeSpan.FromSeconds(component.ExitDelay) : null);

        // Perform phase on the user performing the action, not the provider entity.
        Phase(args.Performer);
        args.Handled = true;
    }

    private void Toggle(EntityUid uid, NullPhaseComponent component, bool toggle)
    {
        if (toggle)
        {
            _actionsSystem.AddAction(uid, ref component.PhaseAction, "NullPhaseAction", uid);
            EnsureComp<ShowNullSpaceComponent>(uid);
        }
        else
        {
            _actionsSystem.RemoveAction(uid, component.PhaseAction);
            RemComp<ShowNullSpaceComponent>(uid);
        }
    }

    public bool Phase(EntityUid uid)
    {
        if (TryComp<NullSpaceComponent>(uid, out var ethereal))
        {
            var currentTile = _turf.GetTileRef(Transform(uid).Coordinates);
            // MobMask (not just Impassable) so that windows, shutters, glass airlocks, and anything
            // else a normal mob cannot walk through also prevents exit — not only solid walls.
            if (currentTile != null && _turf.IsTileBlocked(currentTile.Value, CollisionGroup.MobMask))
            {
                _popup.PopupEntity(Loc.GetString("phase-fail-generic"), uid, uid);
                return false;
            }

            if (HasComp<ShadekinComponent>(uid))
            {
                var lightQuery = _lookup.GetEntitiesInRange(uid, 3, flags: LookupFlags.StaticSundries)
                    .Where(x => HasComp<PoweredLightComponent>(x));
                foreach (var light in lightQuery)
                    _ghost.DoGhostBooEvent(light);

                var effect = SpawnAtPosition(ShadekinPhaseInEffect, Transform(uid).Coordinates);
                Transform(effect).LocalRotation = Transform(uid).LocalRotation;
            }

            if (TryComp<NullPhaseComponent>(uid, out var phaseComp))
                phaseComp.VoluntaryExit = true;
            RemComp<NullSpaceComponent>(uid);
        }
        else
        {
            if (_container.IsEntityInContainer(uid))
            {
                _popup.PopupEntity(Loc.GetString("phase-fail-generic"), uid, uid);
                return false;
            }

            foreach (var flasher in _lookup.GetEntitiesInRange<BluespacePulseOnTriggerComponent>(Transform(uid).Coordinates, 30))
            {
                if (!Transform(flasher).Anchored)
                    continue;

                if (_lookup.GetEntitiesInRange(flasher, flasher.Comp.Radius).Contains(uid))
                {
                    _popup.PopupEntity(Loc.GetString("phase-fail-flasher"), uid, uid);
                    return false;
                }
            }

            EnsureComp<NullSpaceComponent>(uid);

            if (HasComp<ShadekinComponent>(uid))
            {
                var lightQuery = _lookup.GetEntitiesInRange(uid, 3, flags: LookupFlags.StaticSundries)
                    .Where(x => HasComp<PoweredLightComponent>(x));
                foreach (var light in lightQuery)
                    _ghost.DoGhostBooEvent(light);

                var effect = SpawnAtPosition(ShadekinPhaseOutEffect, Transform(uid).Coordinates);
                Transform(effect).LocalRotation = Transform(uid).LocalRotation;
            }
        }
        return true;
    }
}
