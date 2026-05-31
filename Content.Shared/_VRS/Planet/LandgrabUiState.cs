using Robust.Shared.Serialization;

namespace Content.Shared._VRS.Planet;

/// <summary>Snapshot of a single saved plot belonging to the player.</summary>
[Serializable, NetSerializable]
public sealed class SavedPlotInfo
{
    public string SlotName = string.Empty;
    /// <summary>Tile width of the saved grid's bounding box.</summary>
    public int Width;
    /// <summary>Tile height of the saved grid's bounding box.</summary>
    public int Height;
    /// <summary>Total tile count (used for load cost).</summary>
    public int TileCount;
    /// <summary>Computed cost to load this plot at the current location.</summary>
    public int LoadCost;
}

/// <summary>
/// State broadcast to the cartridge UI fragment.
/// </summary>
[Serializable, NetSerializable]
public sealed class LandgrabUiState : BoundUserInterfaceState
{
    /// <summary>True when the player is on a valid planet map.</summary>
    public bool OnValidPlanet;

    /// <summary>Friendly planet name (or status string when not on a planet).</summary>
    public string PlanetName = string.Empty;

    /// <summary>Current player balance in credits.</summary>
    public int Balance;

    /// <summary>Cost to purchase an empty plot at current location.</summary>
    public int PurchaseCost;

    /// <summary>Default plot size (tiles).</summary>
    public int PlotSize;

    /// <summary>Player's current world position on the planet (for the preview overlay). NaN when invalid.</summary>
    public float WorldX;
    public float WorldY;

    /// <summary>True if the chosen location has no overlap with existing plots.</summary>
    public bool LocationFree;

    /// <summary>Reason text if LocationFree is false.</summary>
    public string? LocationBlockedReason;

    /// <summary>True when the player already owns a plot.</summary>
    public bool OwnsPlot;

    /// <summary>List of the player's saved outpost slots.</summary>
    public List<SavedPlotInfo> SavedPlots = new();

    /// <summary>True when the player is holding at least one blank coordinate disk that can be engraved.</summary>
    public bool HasBlankDisk;
}

// ── Messages ──────────────────────────────────────────────────────────────────
// All cartridge-bound UI messages must extend CartridgeMessageEvent so that the
// CartridgeLoader can route them to the cartridge entity via SubscribeLocalEvent
// <Comp, CartridgeMessageEvent>.

[Serializable, NetSerializable]
public sealed class LandgrabRefreshMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent { }

[Serializable, NetSerializable]
public sealed class LandgrabPurchaseMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent { }

/// <summary>Engrave a held blank coordinate disk with the player's current planet position.</summary>
[Serializable, NetSerializable]
public sealed class LandgrabWriteDiskMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent
{
    /// <summary>Optional player-supplied label; falls back to coordinates.</summary>
    public string Label = string.Empty;
}

[Serializable, NetSerializable]
public sealed class LandgrabSaveMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent
{
    public string SlotName = string.Empty;
}

[Serializable, NetSerializable]
public sealed class LandgrabLoadMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent
{
    public string SlotName = string.Empty;
}

[Serializable, NetSerializable]
public sealed class LandgrabDeleteSaveMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent
{
    public string SlotName = string.Empty;
}

[Serializable, NetSerializable]
public sealed class LandgrabAbandonMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent { }
