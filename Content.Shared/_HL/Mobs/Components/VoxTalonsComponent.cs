using Robust.Shared.GameStates;
using Content.Shared._HL.Mobs.Systems;

namespace Content.Shared._HL.Mobs.Components;

[RegisterComponent, NetworkedComponent]
[Access(typeof(VoxTalonsSystem))]
public sealed partial class VoxTalonsComponent : Component
{
}

