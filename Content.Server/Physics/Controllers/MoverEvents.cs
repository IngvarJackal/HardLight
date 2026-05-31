using System.Numerics;

// Mono - whole file

namespace Content.Server.Physics.Controllers;

public record struct ShuttleInput(Vector2 Strafe, float Rotation, float Brakes);

/// <summary>
///     Raised on pilots to get inputs given to a shuttle.
///     If GotInput is false, this piloted is removed from input sources.
/// </summary>
[ByRefEvent]
public record struct GetShuttleInputsEvent(float FrameTime, ShuttleInput? Input = null, bool GotInput = false)
{
    // Mono: per-pilot multipliers consumed by MoverController to scale the shuttle's
    // effective angular thrust and linear acceleration. Defaults to 1f so unmodified
    // pilots have no effect on motion.
    public float AngularMul = 1f;
    public float AccelMul = 1f;
}

[ByRefEvent]
public record struct PilotedShuttleRelayedEvent<TEvent>(TEvent Args);
