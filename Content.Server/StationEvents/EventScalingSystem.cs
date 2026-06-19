using Content.Server._NF.Roles.Systems;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents;

/// <summary>
/// HardLight: shared helpers for scaling station-event severity off live crew. Used by the vent-horde and
/// anomaly-storm rules to size their spawns from a department's headcount (trainees excluded) and the number of
/// players currently online.
/// </summary>
public sealed class EventScalingSystem : EntitySystem
{
    [Dependency] private readonly JobTrackingSystem _jobs = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>Active, non-trainee players holding any role in the given department.</summary>
    public int DepartmentHeadcount(ProtoId<DepartmentPrototype> departmentId, ProtoId<JobPrototype>? internRole = null)
    {
        if (!_proto.TryIndex(departmentId, out var department))
            return 0;

        var count = 0;
        foreach (var role in department.Roles)
        {
            if (internRole != null && role == internRole)
                continue;

            count += _jobs.GetNumberOfActiveRoles(role);
        }

        return count;
    }

    /// <summary>Number of players currently in-game (excludes lobby/disconnected sessions).</summary>
    public int ActivePlayers()
    {
        var count = 0;
        foreach (var session in _player.Sessions)
        {
            if (session.State.Status == SessionStatus.InGame)
                count++;
        }

        return count;
    }
}
