using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._VRS.Planet;

/// <summary>
/// Networked list of dungeons that have been procedurally spawned on a planet.
/// Lives on the planet's map entity so clients (e.g. the shuttle console
/// preview) can draw markers for them even when the underlying biome chunks
/// containing the dungeon haven't been loaded by that client yet.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PlanetDungeonRegistryComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<DungeonMarker> Dungeons = new();
}

/// <summary>
/// One placed dungeon. <see cref="Position"/> is the world tile center on the
/// planet's grid; <see cref="Name"/> is the dungeon-config ID (e.g. "NFCaveFactory")
/// used for the label.
/// </summary>
[Serializable, NetSerializable]
public record struct DungeonMarker(Vector2 Position, string Name);
