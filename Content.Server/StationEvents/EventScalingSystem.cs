using System;
using Content.Server._NF.Roles.Systems;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.StationEvents;

/// <summary>
/// HardLight: shared helpers for scaling station-event severity off live crew. Used by the vent-horde rule (and
/// meant to be reused by future "scale off a department" events such as the anomaly storm) to size spawns or
/// severity from a department's headcount plus the number of players online.
/// <para>
/// Callers pass the department they scale off (e.g. <c>Security</c> for vent hordes, <c>Science</c> for the anomaly
/// storm) so the same logic serves every event. If this grows, lift it into a dedicated scaling component.
/// </para>
/// </summary>
public sealed class EventScalingSystem : EntitySystem
{
    [Dependency] private readonly JobTrackingSystem _jobs = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

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

    /// <summary>
    /// Crew-scaled count: <c>clamp(round(deptHeadcount * multiplier + activePlayers * perPlayer), minCount,
    /// maxCount)</c>. The reusable scaling rule — pass the department and tuning for the event. The multiplier is
    /// rolled between <paramref name="multiplierMin"/> and <paramref name="multiplierMax"/> when both are set; if
    /// only one is set it is used as a static multiplier; if neither is set crew scaling is skipped and the count is
    /// a flat random roll in [<paramref name="minCount"/>, <paramref name="maxCount"/>].
    /// </summary>
    public int ScaledCount(
        ProtoId<DepartmentPrototype> department,
        ProtoId<JobPrototype>? internRole,
        float? multiplierMin,
        float? multiplierMax,
        float perPlayer,
        int minCount,
        int maxCount)
    {
        // No multiplier configured: skip crew scaling and roll a flat count in [minCount, maxCount].
        if (multiplierMin == null && multiplierMax == null)
            return _random.Next(minCount, maxCount + 1);

        // Both bounds set -> roll between them; exactly one set -> use it as a static multiplier.
        var multiplier = multiplierMin != null && multiplierMax != null
            ? _random.NextFloat(multiplierMin.Value, multiplierMax.Value)
            : multiplierMin ?? multiplierMax!.Value;

        var headcount = DepartmentHeadcount(department, internRole);
        var scaled = headcount * multiplier + ActivePlayers() * perPlayer;
        return Math.Clamp((int)MathF.Round(scaled), minCount, maxCount);
    }
}
