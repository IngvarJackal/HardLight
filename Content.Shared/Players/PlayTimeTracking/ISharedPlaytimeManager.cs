using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Player;

namespace Content.Shared.Players.PlayTimeTracking;

public interface ISharedPlaytimeManager
{
    /// <summary>
    /// Tries to get loaded playtimes for the session.
    /// </summary>
    bool TryGetPlayTimes(ICommonSession session, [NotNullWhen(true)] out IReadOnlyDictionary<string, TimeSpan>? playTimes);

    /// <summary>
    /// Gets the loaded playtimes for the session.
    /// </summary>
    IReadOnlyDictionary<string, TimeSpan> GetPlayTimes(ICommonSession session);
}

