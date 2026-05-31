using Content.Shared.Access;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Shared.CM14.Xenos;

// TODO CM14 split up this component
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoComponent : Component
{
    // Evolution
    [DataField]
    public TimeSpan EvolveIn;

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public List<EntProtoId> EvolvesTo = new();

    [DataField]
    public EntProtoId EvolveActionId = "ActionXenoEvolve";

    // Weeds prototype to spawn; use EntProtoId instead of a custom serializer to avoid missing type errors.
    [ViewVariables(VVAccess.ReadWrite), DataField("Weedprototype")]
    public EntProtoId Weedprototype = "XenoWeeds";
    [DataField]
    public EntityUid? EvolveAction;

    // AI auto-evolution scheduling (server-side): when set, XenoSystem.Update will auto-evolve after this time.
    [DataField]
    public TimeSpan? PendingAutoEvolveTime;

    // Actions
    [DataField, AutoNetworkedField]
    public List<EntProtoId> ActionIds = new();

    [DataField]
    public Dictionary<EntProtoId, EntityUid> Actions = new();

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int? OriginalDrawDepth;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool AllowPlantWeeds = true;

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan AcidDelay = TimeSpan.FromSeconds(5);

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string DevourContainerId = "cm_xeno_devour";

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan DevourDelay = TimeSpan.FromSeconds(5);

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier RegurgitateSound = new SoundPathSpecifier("/Audio/.CM14/Xeno/alien_drool2.ogg");

    // Resting regen
    [DataField]
    public DamageSpecifier? RestHealing;

    [DataField]
    public float RestHealingCritMultiplier = 1.5f;

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId TailAnimationId = "WeaponArcThrust";

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float TailRange = 3;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier TailDamage = new();

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier TailHitSound = new SoundCollectionSpecifier("CM14XenoTailSwipe");

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float BuildRange = 1;

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public List<EntProtoId> CanBuild = new();

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId? BuildChoice;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan BuildDelay = TimeSpan.FromSeconds(4);

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<AccessLevelPrototype>> AccessLevels = new() { "Xeno" };
}
