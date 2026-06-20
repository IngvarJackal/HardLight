using Content.Server.StationEvents.Events;
using Content.Shared.EntityTable.EntitySelectors;

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
    // clamp(round(deptHeadcount * multiplier + activePlayers * PerPlayer), MinCount, MaxCount), and the Table is
    // rolled that many times (so the Table's own Rolls should be 1). The department itself is a constant on
    // VentHordeRule (security); see EventScalingSystem.ScaledCount. The multiplier depends on which bounds are set:
    //   both MultiplierMin and MultiplierMax -> random(MultiplierMin, MultiplierMax)
    //   only one of them                     -> that value, used as a static multiplier
    //   neither                              -> no crew scaling; the count is a flat random roll in [MinCount, MaxCount]

    /// <summary>
    /// Whether to size the swarm off live crew (the default). False keeps the legacy behaviour of letting the
    /// <see cref="Table"/>'s own Rolls decide the count — used by flavor swarms.
    /// </summary>
    [DataField]
    public bool Scaled = true;

    /// <summary>
    /// Lower bound of the multiplier applied to the department headcount. When both bounds are set the multiplier is
    /// rolled between them; when only one bound is set it is used as a static multiplier; when neither is set the
    /// count is a flat random roll in [<see cref="MinCount"/>, <see cref="MaxCount"/>] with no crew scaling.
    /// </summary>
    [DataField]
    public float? MultiplierMin;

    /// <inheritdoc cref="MultiplierMin"/>
    [DataField]
    public float? MultiplierMax;

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
