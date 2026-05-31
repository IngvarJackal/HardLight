using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._VRS.Planet;

/// <summary>
/// VRS-side companion to <c>ShuttleDestinationCoordinatesComponent</c>.
/// Stamped onto a coordinate disk by the Landgrab PDA cartridge so the disk
/// remembers a specific world position on a planet, plus a human label, in
/// addition to the destination map. Read by the shuttle console UI to render
/// a clickable landing marker over the planet preview.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LandgrabCoordinatesComponent : Component
{
    /// <summary>The planet map this disk targets.</summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public NetEntity Destination;

    /// <summary>World-space landing position on <see cref="Destination"/>.</summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public Vector2 Offset;

    /// <summary>Human-readable label shown on the marker (e.g. "Bob's outpost").</summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public string Label = string.Empty;
}
