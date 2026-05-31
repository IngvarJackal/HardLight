using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server._VRS.Planet;
using Content.Shared.Popups;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Content.Shared.Shuttles.UI.MapObjects;
using Content.Shared._VRS.Planet;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Content.Server.Station.Components;
using System.Linq;
using System.Numerics;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    private void InitializeFTL()
    {
        SubscribeLocalEvent<FTLBeaconComponent, ComponentStartup>(OnBeaconStartup);
        SubscribeLocalEvent<FTLBeaconComponent, AnchorStateChangedEvent>(OnBeaconAnchorChanged);

        SubscribeLocalEvent<FTLExclusionComponent, ComponentStartup>(OnExclusionStartup);
    }

    private void OnExclusionStartup(Entity<FTLExclusionComponent> ent, ref ComponentStartup args)
    {
        RefreshShuttleConsoles();
    }

    private void OnBeaconStartup(Entity<FTLBeaconComponent> ent, ref ComponentStartup args)
    {
        RefreshShuttleConsoles();
    }

    private void OnBeaconAnchorChanged(Entity<FTLBeaconComponent> ent, ref AnchorStateChangedEvent args)
    {
        RefreshShuttleConsoles();
    }

    private void OnBeaconFTLMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleFTLBeaconMessage args)
    {
        var beaconEnt = GetEntity(args.Beacon);
        if (!_xformQuery.TryGetComponent(beaconEnt, out var targetXform))
        {
            return;
        }

        var nCoordinates = new NetCoordinates(GetNetEntity(targetXform.ParentUid), targetXform.LocalPosition);
        if (targetXform.ParentUid == EntityUid.Invalid)
        {
            nCoordinates = new NetCoordinates(GetNetEntity(beaconEnt), targetXform.LocalPosition);
        }

        // Check target exists
        if (!_shuttle.CanFTLBeacon(nCoordinates))
        {
            return;
        }

        var angle = args.Angle.Reduced();
        var targetCoordinates = new EntityCoordinates(targetXform.MapUid!.Value, _transform.GetWorldPosition(targetXform));

        ConsoleFTL(ent, targetCoordinates, angle, targetXform.MapID);
    }

    private void OnPositionFTLMessage(Entity<ShuttleConsoleComponent> entity, ref ShuttleConsoleFTLPositionMessage args)
    {
        var mapUid = _mapSystem.GetMap(args.Coordinates.MapId);

        // If it's beacons only block all position messages.
        if (!Exists(mapUid) || _shuttle.IsBeaconMap(mapUid))
        {
            return;
        }

        var targetCoordinates = new EntityCoordinates(mapUid, args.Coordinates.Position);
        var angle = args.Angle.Reduced();
        ConsoleFTL(entity, targetCoordinates, angle, args.Coordinates.MapId);
    }

    private void OnStationDockFTLMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleFTLStationDockMessage args)
    {
        var stationEnt = GetEntity(args.Station);
        if (!Exists(stationEnt))
        {
            return;
        }

        // Get the console's shuttle
        var consoleUid = GetDroneConsole(ent.Owner);
        if (consoleUid == null)
            return;

        if (!_xformQuery.TryGetComponent(consoleUid.Value, out var shuttleXform) ||
            !TryComp<ShuttleComponent>(shuttleXform.GridUid, out var shuttleComp))
        {
            return;
        }

        // Get the station's grids for docking
        if (!TryComp<StationDataComponent>(stationEnt, out var stationData) || stationData.Grids.Count == 0)
        {
            return;
        }

        var targetGrid = stationData.Grids.First(); // Use the first/main grid as the docking target

        // Use TryFTLDock for proper station docking - same mechanism as shipyard
        _shuttle.TryFTLDock(shuttleXform.GridUid!.Value, shuttleComp, targetGrid);
    }

    private void GetBeacons(ref List<ShuttleBeaconObject>? beacons)
    {
        var beaconQuery = AllEntityQuery<FTLBeaconComponent>();

        while (beaconQuery.MoveNext(out var destUid, out _))
        {
            var meta = _metaQuery.GetComponent(destUid);
            var name = meta.EntityName;

            if (string.IsNullOrEmpty(name))
                name = Loc.GetString("shuttle-console-unknown");

            // Can't travel to same map (yet)
            var destXform = _xformQuery.GetComponent(destUid);
            beacons ??= new List<ShuttleBeaconObject>();
            beacons.Add(new ShuttleBeaconObject(GetNetEntity(destUid), GetNetCoordinates(destXform.Coordinates), name));
        }
    }

    private void GetExclusions(ref List<ShuttleExclusionObject>? exclusions)
    {
        var query = AllEntityQuery<FTLExclusionComponent, TransformComponent>();

        while (query.MoveNext(out var comp, out var xform))
        {
            if (!comp.Enabled)
                continue;

            exclusions ??= new List<ShuttleExclusionObject>();
            exclusions.Add(new ShuttleExclusionObject(GetNetCoordinates(xform.Coordinates), comp.Range, Loc.GetString("shuttle-console-exclusion")));
        }
    }

    private void GetStations(ref List<ShuttleStationObject>? stations)
    {
        var stationQuery = AllEntityQuery<StationDataComponent, TransformComponent>();

        while (stationQuery.MoveNext(out var stationUid, out var stationData, out var xform))
        {
            var meta = _metaQuery.GetComponent(stationUid);
            var name = meta.EntityName;

            if (string.IsNullOrEmpty(name))
                name = Loc.GetString("shuttle-console-unknown");

            // Add station as FTL dock target
            stations ??= new List<ShuttleStationObject>();
            stations.Add(new ShuttleStationObject(GetNetEntity(stationUid), GetNetCoordinates(xform.Coordinates), $"🏭 {name}"));
        }
    }

    /// <summary>
    /// Handles shuttle console FTLs.
    /// </summary>
    private void ConsoleFTL(Entity<ShuttleConsoleComponent> ent, EntityCoordinates targetCoordinates, Angle targetAngle, MapId targetMap)
    {
        var consoleUid = GetDroneConsole(ent.Owner);

        if (consoleUid == null)
            return;

        var shuttleUid = _xformQuery.GetComponent(consoleUid.Value).GridUid;

        if (!TryComp(shuttleUid, out ShuttleComponent? shuttleComp))
            return;

        if (shuttleComp.Enabled == false)
            return;

        // Check shuttle can even FTL
        if (!_shuttle.CanFTL(shuttleUid.Value, out var reason))
        {
            // TODO: Session popup
            return;
        }

        // Check shuttle can FTL to this target.
        if (!_shuttle.CanFTLTo(shuttleUid.Value, targetMap, ent))
        {
            return;
        }

        List<ShuttleExclusionObject>? exclusions = null;
        GetExclusions(ref exclusions);

        if (!_shuttle.FTLFree(shuttleUid.Value, targetCoordinates, targetAngle, exclusions))
        {
            return;
        }

        // VRS: block FTL within 100m of any procedurally-spawned dungeon on the
        // target map. Dungeon positions are stored in grid-local tile coords on
        // the planet's primary grid; planet grids sit at the map origin so they
        // align with the map-world coordinates used by FTL targeting. Hostile
        // mobs do NOT block landing — any mobs caught under the shuttle on
        // arrival are squashed by PlanetSpawnerSystem's FTL-completed handler.
        if (IsInsideContractNoLandingRadius(targetMap, targetCoordinates.Position))
        {
            _popup.PopupEntity(Loc.GetString("shuttle-console-ftl-contract-too-close"), ent.Owner, PopupType.MediumCaution);
            return;
        }

        if (TryComp<PlanetDungeonRegistryComponent>(_mapSystem.GetMap(targetMap), out var dungeonRegistry))
        {
            const float dungeonExclusionRange = 100f;
            var rangeSq = dungeonExclusionRange * dungeonExclusionRange;
            var targetPos = targetCoordinates.Position;
            foreach (var dungeon in dungeonRegistry.Dungeons)
            {
                if (Vector2.DistanceSquared(targetPos, dungeon.Position) <= rangeSq)
                {
                    _popup.PopupEntity(Loc.GetString("shuttle-console-ftl-dungeon-too-close"), ent.Owner, PopupType.MediumCaution);
                    return;
                }
            }
        }

        if (!TryComp(shuttleUid.Value, out PhysicsComponent? shuttlePhysics))
        {
            return;
        }

        // Client sends the "adjusted" coordinates and we adjust it back to get the actual transform coordinates.
        var adjustedCoordinates = targetCoordinates.Offset(targetAngle.RotateVec(-shuttlePhysics.LocalCenter));

        var tagEv = new FTLTagEvent();
        RaiseLocalEvent(shuttleUid.Value, ref tagEv);

        var ev = new ShuttleConsoleFTLTravelStartEvent(ent.Owner);
        RaiseLocalEvent(ref ev);

        _shuttle.FTLToCoordinates(shuttleUid.Value, shuttleComp, adjustedCoordinates, targetAngle);
    }

    private bool IsInsideContractNoLandingRadius(MapId mapId, Vector2 targetPosition)
    {
        var query = EntityQueryEnumerator<PlanetEventFTLOverrideComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var overrideComp, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var center = _transform.GetWorldPosition(xform); // Fix: Use world position instead of parent-local
            var rangeSq = overrideComp.NoLandingRadius * overrideComp.NoLandingRadius;
            if (Vector2.DistanceSquared(targetPosition, center) <= rangeSq)
                return true;
        }

        return false;
    }
}
