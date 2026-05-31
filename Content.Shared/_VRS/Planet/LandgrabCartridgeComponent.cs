using Robust.Shared.GameObjects;

namespace Content.Shared._VRS.Planet;

/// <summary>
/// Marker for a Landgrab PDA cartridge. The actual logic lives server-side in
/// <c>LandgrabCartridgeSystem</c>; this component lets the system find cartridge
/// instances and is needed to wire up the UIFragment.
/// </summary>
[RegisterComponent]
public sealed partial class LandgrabCartridgeComponent : Component
{
}
