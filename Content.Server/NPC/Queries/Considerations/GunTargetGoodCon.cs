// VRS (Triad #3732)
namespace Content.Server.NPC.Queries.Considerations;

/// <summary>
/// Utility consideration that evaluates whether a target is a good gun target,
/// allowing shooting through obstacles if they can be destroyed within the threshold.
/// </summary>
public sealed partial class GunTargetGoodCon : UtilityConsideration
{
    /// <summary>
    /// Number of shots needed to destroy an obstacle for the NPC to consider shooting through it.
    /// </summary>
    [DataField]
    public float ShootThroughThreshold = 2f;
}
