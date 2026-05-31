using Content.Shared.RCD.Systems;
using Content.Shared.Atmos.Components; // Starlight-edit: RPLD/RPD layered placement support
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Shared.RCD.Components;

/// <summary>
/// Main component for the RCD
/// Optionally uses LimitedChargesComponent.
/// Charges can be refilled with RCD ammo
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RCDSystem))]
public sealed partial class RCDComponent : Component
{
    /// <summary>
    /// List of RCD prototypes that the device comes loaded with
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<RCDPrototype>> AvailablePrototypes { get; set; } = new();

    /// <summary>
    /// Sound that plays when a RCD operation successfully completes
    /// </summary>
    [DataField]
    public SoundSpecifier SuccessSound { get; set; } = new SoundPathSpecifier("/Audio/Items/deconstruct.ogg");

    /// <summary>
    /// The ProtoId of the currently selected RCD prototype
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<RCDPrototype> ProtoId { get; set; } = "Invalid";

    /// <summary>
    /// The direction constructed entities will face upon spawning
    /// </summary>
    [DataField, AutoNetworkedField]
    public Direction ConstructionDirection
    {
        get => _constructionDirection;
        set
        {
            _constructionDirection = value;
            ConstructionTransform = new Transform(new(), _constructionDirection.ToAngle());
        }
    }

    private Direction _constructionDirection = Direction.South;

    /// <summary>
    /// Returns a rotated transform based on the specified ConstructionDirection
    /// </summary>
    /// <remarks>
    /// Contains no position data
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public Transform ConstructionTransform { get; private set; }

    // Frontier: ship-based RCDs
    /// <summary>
    /// Frontier - Shipyard RCD
    /// A flag that limits RCD to the authorized ships.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsShipyardRCD;

    /// <summary>
    /// Frontier - Shipyard RCD
    /// The uid to which this RCD is limited to be used on.
    /// </summary>
    public EntityUid? LinkedShuttleUid = null;
    // End Frontier: ship-based RCDs

    // Starlight: RPLD
    /// <summary>
    /// Indicates whether this is an RPLD (plumbing)
    /// </summary>
    [DataField("isRPLD"), AutoNetworkedField]
    public bool IsRPLD { get; set; } = false;

    // Starlight: RPLD
    /// <summary>
    /// Last free-mode layer selected on the client.
    /// Used by the server as the authoritative layer when placing layered pipes in Free mode.
    /// </summary>
    [DataField]
    public AtmosPipeLayer? LastSelectedLayer { get; set; } = null;
}
