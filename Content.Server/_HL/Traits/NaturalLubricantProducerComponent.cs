using Content.Shared.FixedPoint;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._HL.Traits;

/// <summary>
/// HardLight: "sexual lubricant" (precum / pussy juice) producer. Purely cosmetic - when
/// <see cref="Leaking"/> is toggled on (via an action), it silently spills <see cref="Volume"/>
/// units of lubricant onto the floor every <see cref="LeakIntervalMin"/>-<see cref="LeakIntervalMax"/>
/// seconds. There is no body reservoir (leaking is the only interaction), so idle producers do no
/// per-tick work. Internally "NaturalLubricant" for save/consent compatibility.
/// </summary>
[RegisterComponent, Access(typeof(NaturalLubricantProducerSystem))]
public sealed partial class NaturalLubricantProducerComponent : Component
{
    [DataField]
    public ProtoId<ReagentPrototype> ReagentId = "NaturalLubricant";

    /// <summary>Units of lubricant spilled onto the floor per leak.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("volume")]
    public FixedPoint2 Volume = 1;

    /// <summary>Whether the silent floor leak is currently active.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public bool Leaking = false;

    /// <summary>Minimum/maximum seconds between silent floor leaks while leaking is on.</summary>
    [DataField]
    public float LeakIntervalMin = 3f;

    [DataField]
    public float LeakIntervalMax = 6f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextLeak = TimeSpan.FromSeconds(0);

    /// <summary>The toggle-leaking action granted to the producer.</summary>
    [DataField]
    public EntProtoId ToggleAction = "ActionToggleLeaking";

    [DataField]
    public EntityUid? ToggleActionEntity;
}
