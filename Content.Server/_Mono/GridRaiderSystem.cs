using System.Linq;
using Content.Shared._Mono;
using Content.Shared._Mono.NoHack;
using Content.Shared._Mono.NoDeconstruct;
using Content.Shared.Doors.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Server._Mono;

/// <summary>
/// System that handles the GridRaiderComponent, which applies NoHack and NoDeconstruct to entities with Door and/or VendingMachine components on a grid.
/// Protection is applied once during initialization and remains until the component is removed.
/// </summary>
// VRS: Ported from Triad_Sector — prevents POI infrastructure griefing.
public sealed class GridRaiderSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridRaiderComponent, MapInitEvent>(OnGridRaiderMapInit);
        SubscribeLocalEvent<GridRaiderComponent, ComponentShutdown>(OnGridRaiderShutdown);
    }

    private void OnGridRaiderMapInit(EntityUid uid, GridRaiderComponent component, MapInitEvent args)
    {
        if (!HasComp<MapGridComponent>(uid))
        {
            Log.Warning($"GridRaiderComponent applied to non-grid entity {ToPrettyString(uid)}");
            return;
        }

        ApplyInitialProtection(uid, component);
    }

    private void OnGridRaiderShutdown(EntityUid uid, GridRaiderComponent component, ComponentShutdown args)
    {
        foreach (var entity in component.ProtectedEntities.ToList())
        {
            if (EntityManager.EntityExists(entity))
                RemoveProtection(entity);
        }

        component.ProtectedEntities.Clear();
    }

    private void ApplyInitialProtection(EntityUid gridUid, GridRaiderComponent component)
    {
        var allEntitiesOnGrid = _lookup.GetEntitiesIntersecting(gridUid).ToHashSet();

        foreach (var entity in allEntitiesOnGrid)
        {
            if (entity == gridUid || _container.IsEntityInContainer(entity))
                continue;

            var shouldProtect = false;
            var hackProtect = true;

            if (component.ProtectDoors && HasComp<DoorComponent>(entity))
                shouldProtect = true;

            if (component.ProtectVendingMachines && HasComp<VendingMachineComponent>(entity))
            {
                shouldProtect = true;
                hackProtect = false;
            }

            if (shouldProtect)
                ApplyProtection(entity, component, hackProtect);
        }
    }

    private void ApplyProtection(EntityUid entityUid, GridRaiderComponent component, bool hackProtect = true, bool deconProtect = true)
    {
        if (component.ProtectedEntities.Contains(entityUid))
            return;

        if (hackProtect)
            EnsureComp<NoHackComponent>(entityUid);
        if (deconProtect)
            EnsureComp<NoDeconstructComponent>(entityUid);

        component.ProtectedEntities.Add(entityUid);
    }

    private void RemoveProtection(EntityUid entityUid)
    {
        RemComp<NoHackComponent>(entityUid);
        RemComp<NoDeconstructComponent>(entityUid);
    }
}
