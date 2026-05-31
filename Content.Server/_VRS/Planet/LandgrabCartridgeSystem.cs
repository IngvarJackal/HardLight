using System.Numerics;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Shared._NF.Bank.Components;
using Content.Shared._VRS.Planet;
using Content.Shared.CartridgeLoader;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._VRS.Planet;

/// <summary>
/// Handles the Landgrab PDA cartridge: relays UI events to <see cref="LandgrabPlotService"/>
/// and pushes updated state back to the cartridge UI fragment.
/// </summary>
public sealed class LandgrabCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem? _cartridgeLoader = default!;
    [Dependency] private readonly LandgrabPlotService _plots = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LandgrabCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<LandgrabCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnUiReady(Entity<LandgrabCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateUi(ent, args.Loader);
    }

    private void OnMessage(Entity<LandgrabCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        // Resolve player session/ckey from the cartridge user (the wielder of the PDA).
        if (!_playerManager.TryGetSessionByEntity(args.Actor, out var session))
            return;
        var ckey = session.Name;

        switch (args)
        {
            case LandgrabRefreshMessage:
                break;

            case LandgrabPurchaseMessage:
                HandlePurchase(ent, args.Actor, ckey, session);
                break;

            case LandgrabWriteDiskMessage write:
                HandleWriteDisk(args.Actor, write.Label);
                break;

            case LandgrabSaveMessage save:
                HandleSave(ent, args.Actor, ckey, save.SlotName);
                break;

            case LandgrabLoadMessage load:
                HandleLoad(ent, args.Actor, ckey, session, load.SlotName);
                break;

            case LandgrabDeleteSaveMessage del:
                if (!_plots.TryDeleteSave(ckey, del.SlotName, out var delReason) && delReason != null)
                    Popup(args.Actor, Loc.GetString(delReason));
                break;

            case LandgrabAbandonMessage:
                if (!_plots.TryAbandonPlot(ckey, out var abReason) && abReason != null)
                    Popup(args.Actor, Loc.GetString(abReason));
                else
                    Popup(args.Actor, Loc.GetString("landgrab-abandoned"));
                break;
        }

        // Always push a fresh state after handling any message.
        // The loader entity is encoded in args.LoaderUid; resolve it.
        var loaderUid = GetEntity(args.LoaderUid);
        UpdateUi(ent, loaderUid);
    }

    // ── Action helpers ────────────────────────────────────────────────────────

    private void HandlePurchase(Entity<LandgrabCartridgeComponent> ent, EntityUid actor, string ckey, ICommonSession session)
    {
        if (!TryGetPlanetLocation(actor, out var mapId, out var worldPos))
        {
            Popup(actor, Loc.GetString("landgrab-not-on-planet"));
            return;
        }

        if (!_plots.TryPurchasePlot(ckey, session.Name, actor, mapId, worldPos, out var reason))
        {
            if (reason != null)
                Popup(actor, Loc.GetString(reason));
            return;
        }

        Popup(actor, Loc.GetString("landgrab-purchased"));
    }

    private void HandleSave(Entity<LandgrabCartridgeComponent> ent, EntityUid actor, string ckey, string slotName)
    {
        if (!_plots.TryGetPlayerPlot(ckey, out var gridUid, out _))
        {
            Popup(actor, Loc.GetString("landgrab-no-plot"));
            return;
        }

        if (!_plots.TrySavePlot(ckey, gridUid, slotName, out var reason))
        {
            if (reason != null)
                Popup(actor, Loc.GetString(reason));
            return;
        }

        Popup(actor, Loc.GetString("landgrab-saved", ("slot", slotName)));
    }

    private void HandleLoad(Entity<LandgrabCartridgeComponent> ent, EntityUid actor, string ckey,
        ICommonSession session, string slotName)
    {
        if (!TryGetPlanetLocation(actor, out var mapId, out var worldPos))
        {
            Popup(actor, Loc.GetString("landgrab-not-on-planet"));
            return;
        }

        if (!_plots.TryGetRegistry(mapId, out var registry))
        {
            Popup(actor, Loc.GetString("landgrab-not-on-planet"));
            return;
        }

        // Recompute load cost from saved metadata.
        var saves = _plots.ListSavedPlots(ckey, registry!.LoadCostBase, registry.LoadCostPerTile);
        var info = saves.Find(s => s.SlotName == slotName);
        if (info == null)
        {
            Popup(actor, Loc.GetString("landgrab-no-save"));
            return;
        }

        if (!_plots.TryLoadPlot(ckey, session.Name, actor, mapId, worldPos, slotName, info.LoadCost, out var reason))
        {
            if (reason != null)
                Popup(actor, Loc.GetString(reason));
            return;
        }

        Popup(actor, Loc.GetString("landgrab-loaded", ("slot", slotName)));
    }

    /// <summary>
    /// Engraves a blank coordinate disk in the player's hand with their current
    /// planet position. The disk gains both the upstream
    /// <see cref="ShuttleDestinationCoordinatesComponent"/> (so it shows up in
    /// shuttle consoles) and our <see cref="LandgrabCoordinatesComponent"/>
    /// carrying the per-disk landing offset + label.
    /// </summary>
    private void HandleWriteDisk(EntityUid actor, string requestedLabel)
    {
        if (!TryGetPlanetLocation(actor, out var mapId, out var worldPos) ||
            !_plots.TryGetRegistry(mapId, out var registry))
        {
            Popup(actor, Loc.GetString("landgrab-not-on-planet"));
            return;
        }

        if (!TryFindBlankDisk(actor, out var disk))
        {
            Popup(actor, Loc.GetString("landgrab-no-disk"));
            return;
        }

        var mapUid = _mapManager.GetMapEntityId(mapId);
        var mapNet = GetNetEntity(mapUid);

        // Stamp upstream component so the disk lights up shuttle consoles' dest list.
        var dest = EnsureComp<ShuttleDestinationCoordinatesComponent>(disk);
        dest.Destination = mapNet;
        Dirty(disk, dest);

        // Stamp our companion component for the precise landing offset + label.
        var lg = EnsureComp<LandgrabCoordinatesComponent>(disk);
        lg.Destination = mapNet;
        lg.Offset = worldPos;
        lg.Label = string.IsNullOrWhiteSpace(requestedLabel)
            ? $"{registry!.PlanetName} ({(int)worldPos.X}, {(int)worldPos.Y})"
            : requestedLabel.Trim();
        Dirty(disk, lg);

        Popup(actor, Loc.GetString("landgrab-disk-written", ("label", lg.Label)));
    }

    /// <summary>
    /// Returns the first held entity that is a coordinate disk (has
    /// <see cref="ShuttleDestinationCoordinatesComponent"/>). Existing engraving
    /// is overwritten on purpose so players can re-target a disk.
    /// </summary>
    private bool TryFindBlankDisk(EntityUid actor, out EntityUid disk)
    {
        disk = EntityUid.Invalid;
        foreach (var held in _hands.EnumerateHeld(actor))
        {
            // The blank CoordinatesDisk prototype already has the destination component.
            if (HasComp<ShuttleDestinationCoordinatesComponent>(held))
            {
                disk = held;
                return true;
            }
        }
        return false;
    }

    // ── State broadcast ───────────────────────────────────────────────────────

    private void UpdateUi(Entity<LandgrabCartridgeComponent> ent, EntityUid loaderUid)
    {
        var state = new LandgrabUiState();

        // Find the player wielding the PDA (the parent of the loader, typically).
        if (!TryGetWielder(loaderUid, out var actor) ||
            !_playerManager.TryGetSessionByEntity(actor, out var session))
        {
            state.OnValidPlanet = false;
            state.PlanetName = Loc.GetString("landgrab-no-user");
            _cartridgeLoader?.UpdateCartridgeUiState(loaderUid, state);
            return;
        }

        var ckey = session.Name;
        if (TryComp<BankAccountComponent>(actor, out var bank))
            state.Balance = bank.Balance;

        // Cheap lookup so the UI can disable the engrave button when the player has nothing to write to.
        state.HasBlankDisk = TryFindBlankDisk(actor, out _);

        if (!TryGetPlanetLocation(actor, out var mapId, out var worldPos) ||
            !_plots.TryGetRegistry(mapId, out var registry))
        {
            state.OnValidPlanet = false;
            state.PlanetName = Loc.GetString("landgrab-not-on-planet");
            // Still list saved plots so the player can see what they have.
            state.SavedPlots = _plots.ListSavedPlots(ckey, 1000, 5);
            _cartridgeLoader?.UpdateCartridgeUiState(loaderUid, state);
            return;
        }

        state.OnValidPlanet = true;
        state.PlanetName = registry!.PlanetName;
        state.PurchaseCost = registry.PurchaseCost;
        state.PlotSize = registry.PlotSize;
        state.WorldX = worldPos.X;
        state.WorldY = worldPos.Y;
        state.LocationFree = _plots.IsLocationFree(mapId, worldPos, registry.PlotSize, registry.MinPlotSpacing,
            out var blockReason);
        state.LocationBlockedReason = blockReason;
        state.OwnsPlot = _plots.TryGetPlayerPlot(ckey, out _, out _);
        state.SavedPlots = _plots.ListSavedPlots(ckey, registry.LoadCostBase, registry.LoadCostPerTile);

        _cartridgeLoader?.UpdateCartridgeUiState(loaderUid, state);
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    /// <summary>The user wielding the PDA — typically the loader's transform parent.</summary>
    private bool TryGetWielder(EntityUid loaderUid, out EntityUid wielder)
    {
        wielder = EntityUid.Invalid;
        if (!TryComp<TransformComponent>(loaderUid, out var xform))
            return false;
        if (xform.ParentUid == EntityUid.Invalid)
            return false;
        wielder = xform.ParentUid;
        return true;
    }

    private bool TryGetPlanetLocation(EntityUid actor, out MapId mapId, out Vector2 worldPos)
    {
        mapId = MapId.Nullspace;
        worldPos = Vector2.Zero;
        if (!TryComp<TransformComponent>(actor, out var xform))
            return false;
        mapId = xform.MapID;
        if (mapId == MapId.Nullspace)
            return false;
        worldPos = _transform.GetWorldPosition(actor);
        return true;
    }

    private void Popup(EntityUid actor, string message)
    {
        _popup.PopupEntity(message, actor, actor);
    }
}
