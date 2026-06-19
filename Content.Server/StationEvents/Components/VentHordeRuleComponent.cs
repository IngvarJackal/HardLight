using Content.Server.StationEvents.Events;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents.Components;

/// <summary>
/// Component used for the vent horde gamerule.
/// Picks a random entity with <see cref="VentCritterSpawnLocationComponent"/>
/// and spawns entities picked from the <see cref="Table"/> on it after a delay.
/// </summary>
[RegisterComponent, Access(typeof(VentHordeRule))]
public sealed partial class VentHordeRuleComponent : Component
{
    /// <summary>
    /// The table of possible mobs to spawn from the vent.
    /// </summary>
    [DataField(required: true)]
    public EntityTableSelector Table = default!;

    /// <summary>
    /// The vent that has been chosen to spawn the entities.
    /// Spawning logic is handled by <see cref="VentHordeSpawnerComponent"/>
    /// </summary>
    [DataField]
    public EntityUid? ChosenVent;

    // HardLight: horde size scales off live crew instead of a fixed RangeNumberSelector. The number of mobs is
    // clamp(round(deptHeadcount * random(MultiplierMin, MultiplierMax) + activePlayers * PerPlayer), MinCount, MaxCount),
    // and the Table is rolled that many times (so the Table's own Rolls should be 1).

    /// <summary>
    /// Department whose (non-trainee) headcount scales the spawn count. Null falls back to the legacy behaviour of
    /// letting the <see cref="Table"/>'s own Rolls decide the count.
    /// </summary>
    [DataField]
    public ProtoId<DepartmentPrototype>? ScalingDepartment = "Security";

    /// <summary>Trainee role in the scaling department, excluded from the headcount.</summary>
    [DataField]
    public ProtoId<JobPrototype>? ScalingInternJob = "SecurityCadet";

    /// <summary>Random multiplier applied to the department headcount.</summary>
    [DataField]
    public float MultiplierMin = 2f;

    /// <inheritdoc cref="MultiplierMin"/>
    [DataField]
    public float MultiplierMax = 4f;

    /// <summary>Extra spawns added per player online, on top of the department-scaled count.</summary>
    [DataField]
    public float PerPlayer = 0.1f;

    /// <summary>Minimum spawn count after scaling.</summary>
    [DataField]
    public int MinCount = 3;

    /// <summary>Maximum spawn count after scaling (guards against runaway swarms).</summary>
    [DataField]
    public int MaxCount = 75;
}
