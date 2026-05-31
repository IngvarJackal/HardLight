using Content.Shared.SprayPainter.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Triad.SprayPainter;

/// <summary>
/// Stores the spray-paint style that was last applied to this entity so it can be
/// re-applied on MapInit (e.g. after a ship is saved and reloaded).
/// The Style field is an EntProtoId pointing to an entity with a PaintableComponent.
/// Ported from Triad Sector #20 (76477fc).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SprayPaintOnMapInitComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId Style = default!;
}
