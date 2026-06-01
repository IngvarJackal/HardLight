// HardLight: per-tick projectile state tracker for diagnosing client/server
// rendering divergence (suspected networking bug where the client renders
// projectiles differently than the authoritative server).
//
// This system runs on BOTH client and server (every IEntitySystem is discovered
// automatically). It walks all networked projectiles each tick and logs their
// world position / rotation / linear velocity via ProjDebug, keyed by NetEntity
// and tick so the two sides' traces of the SAME projectile can be diffed:
//
//   grep "net=<id>" client.log
//   grep "net=<id>" server.log
//
// Throttled per-projectile to keep the logs readable.
// Remove this file (and the ProjDebug call sites) once the bugs are fixed.

using System.Collections.Generic;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Mono.Debugging;

public sealed class ProjectileDebugSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<PhysicsComponent> _physQuery;

    // Per-projectile last-logged time, so a fast-moving swarm doesn't flood the file.
    private readonly Dictionary<EntityUid, TimeSpan> _lastLog = new();
    private TimeSpan _nextPrune;

    private static readonly TimeSpan LogInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();
        _physQuery = GetEntityQuery<PhysicsComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // Bound memory; harmless re-log right after a prune.
        if (now >= _nextPrune)
        {
            _lastLog.Clear();
            _nextPrune = now + PruneInterval;
        }

        var query = EntityQueryEnumerator<ProjectileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (_lastLog.TryGetValue(uid, out var last) && now - last < LogInterval)
                continue;

            _lastLog[uid] = now;

            var worldPos = _xform.GetWorldPosition(xform);
            var worldRot = _xform.GetWorldRotation(xform);
            var linVel = _physQuery.CompOrNull(uid)?.LinearVelocity ?? default;

            ProjDebug.Log("projectile.track",
                $"net={GetNetEntity(uid)} tick={_timing.CurTick.Value} " +
                $"world={ProjDebug.V(worldPos)} rot={ProjDebug.Deg(worldRot)} " +
                $"linVel={ProjDebug.V(linVel)} parent={ToPrettyString(xform.ParentUid)} " +
                $"predicted={_timing.IsFirstTimePredicted}");
        }
    }
}
