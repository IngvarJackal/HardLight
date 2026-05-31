namespace Content.Shared._Mono.Ships.Components;

/// <summary>
/// Marks a shuttle as crewed: a single user cannot have both a shuttle console
/// and a gunnery console open simultaneously on the same grid. Override on a
/// per-user basis with <see cref="AdvancedPilotComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CrewedShuttleComponent : Component
{
    [DataField]
    public List<EntityUid> ShuttleConsoles = new();

    [DataField]
    public List<EntityUid> GunneryConsoles = new();
}
