using System.Numerics;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Components
{
    [RegisterComponent]
    public sealed partial class ShuttleComponent : Component
    {
        [ViewVariables]
        public bool Enabled = true;

        [ViewVariables]
        public Vector2[] CenterOfThrust = new Vector2[4];

        /// <summary>
        /// Thrust gets multiplied by this value if it's for braking.
        /// </summary>
        public const float BrakeCoefficient = 1.5f;

        /// <summary>
        /// Maximum velocity assuming TWR is BaseMaxVelocityTWR.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public float BaseMaxLinearVelocity = 50f; // Mono

        public const float MaxAngularVelocity = 4f;

        /// <summary>
        /// The cached thrust available for each cardinal direction
        /// </summary>
        [ViewVariables]
        public readonly float[] LinearThrust = new float[4];

        /// <summary>
        /// The cached thrust available for each cardinal direction, if all thrusters are T1
        /// </summary>
        [ViewVariables]
        public readonly float[] BaseLinearThrust = new float[4];

        /// <summary>
        /// The thrusters contributing to each direction for impulse.
        /// </summary>
        // No touchy
        public readonly List<EntityUid>[] LinearThrusters = new List<EntityUid>[]
        {
            new(),
            new(),
            new(),
            new(),
        };

        /// <summary>
        /// The thrusters contributing to the angular impulse of the shuttle.
        /// </summary>
        public readonly List<EntityUid> AngularThrusters = new();

        [ViewVariables]
        public float AngularThrust = 0f;

        // <Mono>
        /// <summary>
        /// Multiplier to angular thrust. Set depending on pilot via <c>GetShuttleInputsEvent</c>.
        /// </summary>
        [ViewVariables]
        public float AngularMultiplier = 1f;

        /// <summary>
        /// Multiplier to linear thrust. Set depending on pilot via <c>GetShuttleInputsEvent</c>.
        /// </summary>
        [ViewVariables]
        public float AccelerationMultiplier = 1f;
        // </Mono>

        /// <summary>
        /// A bitmask of all the directions we are considered thrusting.
        /// </summary>
        [ViewVariables]
        public DirectionFlag ThrustDirections = DirectionFlag.None;

        /// <summary>
        /// Base damping modifier applied to the shuttle's physics component when not in FTL.
        /// </summary>
        [DataField]
        public float BodyModifier = 0.25f;

        /// <summary>
        /// Final Damping Modifier for a shuttle.
        /// This value is set to 0 during FTL. And to BodyModifier when not in FTL.
        /// </summary>
        [DataField]
        public float DampingModifier;

        /// <summary>
        /// Optional override for the FTL cooldown for this shuttle.
        /// If not null, then the value will be used instead of the shuttle.cooldown CCVar.
        /// </summary>
        [DataField]
        public TimeSpan? FTLCooldownOverride = null;

        // <HL>
        /// <summary>
        /// Whether the WEP (War Emergency Power) boost is currently active.
        /// </summary>
        [ViewVariables]
        public bool WepBoostActive = false;

        /// <summary>
        /// When the WEP boost expires.
        /// </summary>
        [ViewVariables]
        public TimeSpan WepBoostExpiry = TimeSpan.Zero;

        /// <summary>
        /// The WEP max velocity for this ship (computed from grid size on activation).
        /// </summary>
        [ViewVariables]
        public float WepBoostMaxVelocity = 100f;

        public const float WepBoostDuration = 5f;
        public const float WepBleedDuration = 1f;
        public const float WepCooldownDuration = 30f;

        // Reference grid size for WEP scaling (250 tiles → 100 m/s base).
        public const float WepBaseGridSize = 250f;
        public const float WepBaseVelocity = 100f;
        public const float WepUpperVelocity = 125f;
        public const float WepLowerVelocity = 50f; // server's default speed

        /// <summary>
        /// When the post-WEP velocity bleed-down finishes.
        /// </summary>
        [ViewVariables]
        public TimeSpan WepBleedExpiry = TimeSpan.Zero;

        /// <summary>
        /// When the WEP cooldown expires (WEP cannot be activated before this time).
        /// </summary>
        [ViewVariables]
        public TimeSpan WepCooldownExpiry = TimeSpan.Zero;

        /// <summary>
        /// Entity of the looping wep_buzz audio stream, if active.
        /// </summary>
        public EntityUid? WepAudioStream;

        /// <summary>
        /// Pre-computed thrust multiplier: WepBoostMaxVelocity / WepLowerVelocity. Reset to 1 on expiry.
        /// </summary>
        [ViewVariables]
        public float WepThrustMultiplier = 1f;

        /// <summary>
        /// Whether WEP recharge draw is active.
        /// </summary>
        public bool WepPowerApplied = false;

        /// <summary>
        /// The load (W) currently applied to the console(s) from WEP recharging.
        /// </summary>
        public float WepCurrentLoad = 0f;

        /// <summary>
        /// Last time the ramp step ran (updated every second during recharge).
        /// </summary>
        public TimeSpan WepLastLoadUpdateTime = TimeSpan.Zero;

        // </HL>

        // <Mono>
        /// <summary>
        /// Limit to max velocity set by a shuttle console.
        /// </summary>
        [DataField]
        public float SetMaxVelocity = 125f;

        /// <summary>
        /// At what Thrust-Weight-Ratio should this ship have the base max velocity as its maximum velocity.
        /// </summary>
        [DataField]
        public float BaseMaxVelocityTWR = 8f;

        /// <summary>
        /// How much should TWR affect max velocity.
        /// </summary>
        [DataField]
        public float MaxVelocityScalingExponent = 0.25f; // 16x thrust = 2x max speed

        /// <summary>
        /// Don't allow max velocity to go beyond this value.
        /// </summary>
        [DataField]
        public float UpperMaxVelocity = 140f; // we ball
        // </Mono>
    }
}
