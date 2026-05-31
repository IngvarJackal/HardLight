using Content.Server._Mono.Ships.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.Ships.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.UserInterface;
using Robust.Shared.Network;
using Robust.Server.GameObjects;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ShuttleConsoleSystem _shuttleConsoleSystem = default!;
    [Dependency] private readonly CrewedShuttleSystem _crewedShuttle = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    private void InitializeConsole()
    {
        SubscribeLocalEvent<FireControlConsoleComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<FireControlConsoleComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleRefreshServerMessage>(OnRefreshServer);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleFireMessage>(OnFire);
        SubscribeLocalEvent<FireControlConsoleComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<FireControlConsoleComponent, ComponentStartup>(OnConsoleStartup);
        SubscribeLocalEvent<FireControlConsoleComponent, ActivatableUIOpenAttemptEvent>(OnConsoleUIOpenAttempt);
    }

    private void OnConsoleUIOpenAttempt(EntityUid uid, FireControlConsoleComponent component, ActivatableUIOpenAttemptEvent args)
    {
        // Mono: on crewed shuttles, deny opening a gunnery console if this user already has
        // a shuttle console open on the same grid (unless they are an AdvancedPilot).
        var shuttle = _xform.GetParentUid(uid);
        var uiOpen = _crewedShuttle.AnyShuttleConsoleActiveByPlayer(shuttle, args.User);
        var forceOne = HasComp<CrewedShuttleComponent>(shuttle) && !HasComp<AdvancedPilotComponent>(args.User);

        if (uiOpen && forceOne)
        {
            args.Cancel();
            _popup.PopupClient(Loc.GetString("shuttle-console-crewed"), args.User);
        }
    }

    private void OnConsoleStartup(EntityUid uid, FireControlConsoleComponent component, ComponentStartup args)
    {
        if (_power.IsPowered(uid))
            TryRegisterConsole(uid, component);
    }

    private void OnPowerChanged(EntityUid uid, FireControlConsoleComponent component, PowerChangedEvent args)
    {
        if (args.Powered)
            TryRegisterConsole(uid, component);
        else
            UnregisterConsole(uid, component);
    }

    private void OnComponentShutdown(EntityUid uid, FireControlConsoleComponent component, ComponentShutdown args)
    {
        UnregisterConsole(uid, component);
    }

    private void OnRefreshServer(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleRefreshServerMessage args)
    {
        if (component.ConnectedServer == null)
        {
            TryRegisterConsole(uid, component);
        }

        if (component.ConnectedServer != null &&
            TryComp<FireControlServerComponent>(component.ConnectedServer, out var server) &&
            server.ConnectedGrid != null)
        {
            RefreshControllables((EntityUid)server.ConnectedGrid);
        }
    }

    private void OnFire(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleFireMessage args)
    {
        if (component.ConnectedServer == null || !TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
            return;

        // Fire the actual weapons
        FireWeapons((EntityUid)component.ConnectedServer, args.Selected, args.Coordinates, server);

        // Raise an event to track the cursor position even when not firing
        var fireEvent = new FireControlConsoleFireEvent(args.Coordinates, args.Selected);
        RaiseLocalEvent(uid, fireEvent);
    }

    public void OnUIOpened(EntityUid uid, FireControlConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void UnregisterConsole(EntityUid console, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(console, ref component))
            return;

        if (component.ConnectedServer == null || !TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
            return;

        server.Consoles.Remove(console);
        component.ConnectedServer = null;
        UpdateUi(console, component);
    }
    private bool TryRegisterConsole(EntityUid console, FireControlConsoleComponent? consoleComponent = null)
    {
        if (!Resolve(console, ref consoleComponent))
            return false;

        var gridServer = TryGetGridServer(console);

        if (gridServer.ServerUid == null || gridServer.ServerComponent == null)
            return false;

        if (gridServer.ServerComponent.Consoles.Add(console) || consoleComponent.ConnectedServer != gridServer.ServerUid)
        {
            consoleComponent.ConnectedServer = gridServer.ServerUid;
            UpdateUi(console, consoleComponent);
            return true;
        }
        else
        {
            return false;
        }
    }

    private void UpdateUi(EntityUid uid, FireControlConsoleComponent? component = null, Dictionary<NetEntity, List<DockingPortState>>? docks = null)
    {
        if (!Resolve(uid, ref component))
            return;

        docks ??= _shuttleConsoleSystem.GetAllDocks();
        NavInterfaceState navState = _shuttleConsoleSystem.GetNavState(uid, docks);

        List<FireControllableEntry> controllables = new();
        if (component.ConnectedServer != null && TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
        {
            foreach (var controllable in server.Controlled)
            {
                var controlled = new FireControllableEntry();
                controlled.NetEntity = EntityManager.GetNetEntity(controllable);
                controlled.Coordinates = GetNetCoordinates(Transform(controllable).Coordinates);
                controlled.Name = MetaData(controllable).EntityName;

                controllables.Add(controlled);
            }
        }

        var array = controllables.ToArray();

        float? shieldHealth = null;
        var consoleGrid = Transform(uid).GridUid;
        if (consoleGrid != null)
        {
            var emitterQuery = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
            while (emitterQuery.MoveNext(out _, out var emitterComp, out var emitterXform))
            {
                if (emitterXform.GridUid != consoleGrid)
                    continue;
                var health = 1f - Math.Clamp(emitterComp.Damage / emitterComp.DamageLimit, 0f, 1f);
                shieldHealth = MathF.Round(health * 100f);
                break;
            }
        }

        var state = new FireControlConsoleBoundInterfaceState(component.ConnectedServer != null, array, navState, shieldHealth);
        _ui.SetUiState(uid, FireControlConsoleUiKey.Key, state);
    }
}
