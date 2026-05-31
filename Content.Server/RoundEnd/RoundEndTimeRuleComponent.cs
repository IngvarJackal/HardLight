namespace Content.Server.RoundEnd;

/// <summary>
/// If a game rule with this component is active, overrides the round-end auto-call time to the value set here.
/// Ported from Triad_Sector.
/// </summary>
[RegisterComponent]
public sealed partial class RoundEndTimeRuleComponent : Component
{
    [DataField]
    public TimeSpan EndAt;
}
