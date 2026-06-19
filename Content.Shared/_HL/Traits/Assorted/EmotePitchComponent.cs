using Robust.Shared.GameStates;

namespace Content.Shared._HL.Traits.Assorted;

/// <summary>
/// Shifts the pitch of this entity's emote sounds by a fixed number of semitones.
/// Added by the voice-pitch traits (low/lowest/high/highest).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class EmotePitchComponent : Component
{
    /// <summary>
    ///     Pitch offset applied to emote sounds, in semitones. Negative deepens, positive raises.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Semitones;
}
