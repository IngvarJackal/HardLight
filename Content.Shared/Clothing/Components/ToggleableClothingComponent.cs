using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Clothing.Components;

/// <summary>
///     This component gives an item an action that will toggle the visibility of character markings (tattoos, scars, etc.).
///     When toggled, it shows/hides markings on the body part that this clothing covers.
/// </summary>
[Access(typeof(ToggleableClothingSystem))]
[RegisterComponent, NetworkedComponent]
public sealed partial class ToggleableClothingComponent : Component
{
    public const string DefaultClothingContainerId = "toggleable-clothing";

    /// <summary>
    ///     Action used to toggle the markings on or off.
    /// </summary>
    [DataField]
    public EntProtoId Action = "ActionToggleSuitPiece";

    // HardLight: persist ActionEntity across save/load. Without [DataField] this is wiped on
    // deserialization; OnMapInit (where EnsureAction normally runs) only fires on entity creation,
    // not on load, so a hardsuit stashed inside a saved apartment loses its toggle action and
    // strip verb until the suit is replaced (issue #1530). The action entity itself lives in
    // ActionsContainerComponent and is persisted as a child, so its EntityUid is remapped
    // correctly on load.
    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>
    ///     The marking prototype ID to toggle. If specified, only this specific marking will be toggled.
    ///     If not specified, all markings on the relevant body part will be toggled.
    /// </summary>
    [DataField]
    public ProtoId<MarkingPrototype>? MarkingPrototype = null;

    /// <summary>
    ///     Legacy support: Default clothing entity prototype to spawn into the clothing container.
    ///     This is kept for backward compatibility with existing hardsuit definitions.
    /// </summary>
    [DataField]
    public EntProtoId? ClothingPrototype = null;

    /// <summary>
    ///     Whether the markings are currently visible or hidden.
    /// </summary>
    [DataField]
    public bool MarkingsVisible = false;

    /// <summary>
    ///     The inventory slot that the clothing is equipped to.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public string Slot = "head";

    /// <summary>
    ///     The inventory slot flags required for this component to function.
    /// </summary>
    [DataField("requiredSlot")]
    public SlotFlags RequiredFlags = SlotFlags.OUTERCLOTHING;

    /// <summary>
    ///     The container that the clothing is stored in when not equipped.
    /// </summary>
    [DataField]
    public string ContainerId = DefaultClothingContainerId;

    [ViewVariables]
    public ContainerSlot? Container;

    /// <summary>
    ///     The Id of the piece of clothing that belongs to this component. Required for map-saving if the clothing is
    ///     currently not inside of the container.
    /// </summary>
    [DataField]
    public EntityUid? ClothingUid;

    /// <summary>
    ///     Time it takes for this clothing to be toggled via the stripping menu verbs. Null prevents the verb from even showing up.
    /// </summary>
    [DataField]
    public TimeSpan? StripDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Text shown in the toggle-clothing verb. Defaults to using the name of the <see cref="ActionEntity"/> action.
    /// </summary>
    [DataField]
    public string? VerbText;
}

[Serializable, NetSerializable]
public sealed class ToggleableClothingComponentState : ComponentState
{
    public readonly EntProtoId Action;
    public readonly NetEntity? ActionEntity;
    public readonly ProtoId<MarkingPrototype>? MarkingPrototype;
    public readonly EntProtoId? ClothingPrototype;
    public readonly bool MarkingsVisible;
    public readonly string Slot;
    public readonly SlotFlags RequiredFlags;
    public readonly string ContainerId;
    public readonly NetEntity? ClothingUid;
    public readonly TimeSpan? StripDelay;
    public readonly string? VerbText;

    public ToggleableClothingComponentState(
        EntProtoId action,
        NetEntity? actionEntity,
        ProtoId<MarkingPrototype>? markingPrototype,
        EntProtoId? clothingPrototype,
        bool markingsVisible,
        string slot,
        SlotFlags requiredFlags,
        string containerId,
        NetEntity? clothingUid,
        TimeSpan? stripDelay,
        string? verbText)
    {
        Action = action;
        ActionEntity = actionEntity;
        MarkingPrototype = markingPrototype;
        ClothingPrototype = clothingPrototype;
        MarkingsVisible = markingsVisible;
        Slot = slot;
        RequiredFlags = requiredFlags;
        ContainerId = containerId;
        ClothingUid = clothingUid;
        StripDelay = stripDelay;
        VerbText = verbText;
    }
}
