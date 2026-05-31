using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._VRS.Planet;

/// <summary>
/// Placed on the planet map entity. Marks the map as one where the Landgrab
/// PDA app can purchase plots, and configures purchase / load pricing.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PlanetPlotRegistryComponent : Component
{
    /// <summary>Friendly name of this planet shown in the Landgrab UI.</summary>
    [DataField]
    public string PlanetName = "Unknown Planet";

    /// <summary>Flat cost (credits) to purchase an empty plot at the player's current location.</summary>
    [DataField]
    public int PurchaseCost = 5000;

    /// <summary>Per-tile cost (credits) added to the load cost when restoring a saved outpost.</summary>
    [DataField]
    public int LoadCostPerTile = 5;

    /// <summary>Flat base cost (credits) added to the load cost when restoring a saved outpost.</summary>
    [DataField]
    public int LoadCostBase = 1000;

    /// <summary>Default plot size in tiles (square edge length) when purchasing.</summary>
    [DataField]
    public int PlotSize = 32;

    /// <summary>Minimum distance (tiles) a new plot must keep from existing plots.</summary>
    [DataField]
    public int MinPlotSpacing = 4;
}
