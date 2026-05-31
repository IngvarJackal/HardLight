namespace Content.Server._Mono.Shuttles.Components;

/// <summary>
/// Modifies how shuttles piloted by this entity drive. Multipliers are applied
/// per-pilot via <see cref="Content.Server.Physics.Controllers.GetShuttleInputsEvent"/>
/// and averaged across active pilots in
/// <see cref="Content.Server.Physics.Controllers.MoverController"/>.
/// </summary>
[RegisterComponent]
public sealed partial class ShuttleBoostingPilotComponent : Component
{
    [DataField]
    public float AngularMultiplier = 1f;

    [DataField]
    public float AccelerationMultiplier = 1f;
}
