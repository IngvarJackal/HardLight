using Content.Server.Administration.Logs;
using Content.Server.Destructible;
using Content.Server.Effects;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Physics;
using Content.Shared.Whitelist; // HardLight
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics; // Mono
using Robust.Shared.Physics.Events; // HardLight - PreventCollideEvent in anti-tunnel raycast
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;

namespace Content.Server.Projectiles;

public sealed class ProjectileSystem : SharedProjectileSystem
{
    [Dependency] private readonly DestructibleSystem _destructibleSystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!; // HardLight

    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;

    private EntityQuery<PhysicsComponent> _physQuery;
    private EntityQuery<FixturesComponent> _fixQuery;

    /// <summary>
    /// Minimum velocity for a projectile to be considered for raycast hit detection.
    /// Projectiles slower than this will rely on standard StartCollideEvent.
    /// </summary>
    private const float MinRaycastVelocity = 75f; // 100->75 Mono

    public override void Initialize()
    {
        base.Initialize();

        // Mono
        _physQuery = GetEntityQuery<PhysicsComponent>();
        _fixQuery = GetEntityQuery<FixturesComponent>();

        // Mono
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override DamageSpecifier? ProjectileCollide(Entity<ProjectileComponent, PhysicsComponent> projectile, EntityUid target, MapCoordinates? collisionCoordinates, bool predicted = false)
    {
        var (uid, component, ourBody) = projectile;
        // Check if projectile is already spent (server-specific check)
        if (component.ProjectileSpent)
            return null;

        if (TryComp<ProjectileTargetWhitelistComponent>(uid, out var targetFilter) // HardLight
            && !_whitelist.CheckBoth(target, targetFilter.Blacklist, targetFilter.Whitelist))
        {
            return null;
        }

        var otherName = ToPrettyString(target);
        // Get damage required for destructible before base applies damage
        var damageRequired = FixedPoint2.Zero;
        if (TryComp(target, out DamageableComponent? damageableComponent))
        {
            damageRequired = _destructibleSystem.DestroyedAt(target);
            damageRequired -= damageableComponent.TotalDamage;
            damageRequired = FixedPoint2.Max(damageRequired, FixedPoint2.Zero);
        }
        var deleted = Deleted(target);

        // Call base implementation to handle damage application and other effects
        var modifiedDamage = base.ProjectileCollide(projectile, target, collisionCoordinates, predicted);

        if (modifiedDamage == null)
        {
            component.ProjectileSpent = true;
            if (component.DeleteOnCollide && component.ProjectileSpent)
                QueueDel(uid);
            return null;
        }

        // Server-specific logic: penetration
        if (component.PenetrationThreshold != 0)
        {
            // If a damage type is required, stop the bullet if the hit entity doesn't have that type.
            if (component.PenetrationDamageTypeRequirement != null)
            {
                var stopPenetration = false;
                foreach (var requiredDamageType in component.PenetrationDamageTypeRequirement)
                {
                    if (!modifiedDamage.DamageDict.Keys.Contains(requiredDamageType))
                    {
                        stopPenetration = true;
                        break;
                    }
                }

                if (stopPenetration)
                    component.ProjectileSpent = true;
            }

            // If the object won't be destroyed, it "tanks" the penetration hit.
            if (modifiedDamage.GetTotal() < damageRequired)
            {
                component.ProjectileSpent = true;
            }

            if (!component.ProjectileSpent)
            {
                component.PenetrationAmount += damageRequired;
                // The projectile has dealt enough damage to be spent.
                if (component.PenetrationAmount >= component.PenetrationThreshold)
                {
                    component.ProjectileSpent = true;
                }
            }
        }
        else
        {
            component.ProjectileSpent = true;
        }

        return modifiedDamage;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var projectileComp, out var physicsComp, out var xform))
        {
            if (projectileComp.ProjectileSpent || TerminatingOrDeleted(uid))
                continue;

            var currentVelocity = physicsComp.LinearVelocity;
            if (currentVelocity.Length() < MinRaycastVelocity)
                continue;

            var lastPosition = _transformSystem.GetWorldPosition(xform, GetEntityQuery<TransformComponent>());
            var rayDirection = currentVelocity.Normalized();
            // Ensure rayDistance is not zero to prevent issues with IntersectRay if frametime or velocity is zero.
            var rayDistance = currentVelocity.Length() * frameTime;
            if (rayDistance <= 0f)
                continue;

            if (!_fixQuery.TryComp(uid, out var fix) || !fix.Fixtures.TryGetValue(ProjectileFixture, out var projFix))
                continue;

            var collisionMask = projFix.CollisionMask;

            var hits = _physics.IntersectRay(xform.MapID,
                new CollisionRay(lastPosition, rayDirection, collisionMask),
                rayDistance,
                uid, // Entity to ignore (self)
                false) // IncludeNonHard = false
                .ToList();

            // P0 instrumentation: only log when the ray actually hit something (rare/interesting).
            var rawHitCount = hits.Count;
            if (rawHitCount > 0)
            {
                TryComp<Content.Shared._Mono.ProjectileGridPhaseComponent>(uid, out var phaseDbg);
                var hitDesc = string.Join("; ", hits.Select(h =>
                    $"{ToPrettyString(h.HitEntity)}@{h.Distance:0.##}grid={ToPrettyString(Transform(h.HitEntity).GridUid ?? default)}"));
                Content.Shared._Mono.Debugging.ProjDebug.Log("raycast.hits",
                    $"net={GetNetEntity(uid)} vel={physicsComp.LinearVelocity.Length():0.#} rayDist={rayDistance:0.##} " +
                    $"resetVel={projectileComp.RaycastResetVelocity} hasPhase={phaseDbg != null} " +
                    $"sourceGrid={ToPrettyString(phaseDbg?.SourceGrid ?? default)} rawHits=[{hitDesc}]");
            }

            // If IgnoreShooter is true, remove the shooter from the list of potential hits.
            if (projectileComp.IgnoreShooter && projectileComp.Shooter.HasValue)
            {
                hits.RemoveAll(hit => hit.HitEntity == projectileComp.Shooter.Value);
            }

            if (TryComp<ProjectileTargetWhitelistComponent>(uid, out var targetFilter)) // HardLight
            {
                hits.RemoveAll(hit => !_whitelist.CheckBoth(hit.HitEntity, targetFilter.Blacklist, targetFilter.Whitelist));
            }

            // HardLight (ported from Triad Sector): the anti-tunnel raycast must respect collision
            // prevention. Without this a ship's own shell "hits" its own shield (a hard
            // BulletImpassable fixture) and gets teleported onto the shield entity - which sits at the
            // grid centre - with its speed clamped to MinRaycastVelocity*0.99 (=74.25). That is the
            // "shells originate from the grid centre / slow to 74 when firing through our shield" bug.
            // Drop any hit a PreventCollideEvent cancels (own-grid phasing, shooter, etc.).
            hits.RemoveAll(hit =>
            {
                var prevented = RaycastHitPrevented(uid, physicsComp, projFix, hit.HitEntity);
                if (rawHitCount > 0)
                    Content.Shared._Mono.Debugging.ProjDebug.Log("raycast.prevent",
                        $"net={GetNetEntity(uid)} hit={ToPrettyString(hit.HitEntity)} prevented={prevented}");
                return prevented;
            });

            if (hits.Count > 0)
            {
                // Process the closest hit
                // IntersectRay results are not guaranteed to be sorted by distance, so we sort them.
                hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                var closestHit = hits.First();

                // teleport us to the actual hit POINT along the ray - NOT the hit entity's origin
                // (for a ship shield that origin is the grid centre, which yanked shells to centre).
                var tpPos = lastPosition + rayDirection * closestHit.Distance;
                Content.Shared._Mono.Debugging.ProjDebug.Log("raycast.teleport",
                    $"net={GetNetEntity(uid)} hit={ToPrettyString(closestHit.HitEntity)} " +
                    $"to={Content.Shared._Mono.Debugging.ProjDebug.V(tpPos)} dist={closestHit.Distance:0.##} " +
                    $"clampVel={projectileComp.RaycastResetVelocity}");
                _transformSystem.SetWorldPosition(uid, tpPos);
                if (projectileComp.RaycastResetVelocity)
                    _physics.SetLinearVelocity(uid, rayDirection * MinRaycastVelocity * 0.99f);

                continue;
            }

            if (rawHitCount > 0)
                Content.Shared._Mono.Debugging.ProjDebug.Log("raycast.passed",
                    $"net={GetNetEntity(uid)} all {rawHitCount} raw hit(s) phased - shell continues unmodified");
        }
    }

    /// <summary>
    /// HardLight (ported from Triad Sector): mirror the engine's ShouldCollide PreventCollide
    /// handshake so the anti-tunnel raycast ignores entities the projectile would actually phase
    /// through - its own grid (ProjectileGridPhaseComponent), shields, the shooter, etc. Returns
    /// true if the hit should be ignored.
    /// </summary>
    private bool RaycastHitPrevented(EntityUid uid, PhysicsComponent body, Fixture projFix, EntityUid hitEnt)
    {
        // No body / no fixtures to collide with -> not a real collision (skip), matching Triad.
        if (!_physQuery.TryComp(hitEnt, out var otherBody) || !_fixQuery.TryComp(hitEnt, out var otherFixtures))
            return true;

        Fixture? hitFix = null;
        foreach (var kv in otherFixtures.Fixtures)
        {
            if (kv.Value.Hard)
            {
                hitFix = kv.Value;
                break;
            }
        }

        if (hitFix == null)
            return true; // nothing hard to actually collide with

        var ourEv = new PreventCollideEvent(uid, hitEnt, body, otherBody, projFix, hitFix);
        RaiseLocalEvent(uid, ref ourEv);
        if (ourEv.Cancelled)
            return true;

        var otherEv = new PreventCollideEvent(hitEnt, uid, otherBody, body, hitFix, projFix);
        RaiseLocalEvent(hitEnt, ref otherEv);
        return otherEv.Cancelled;
    }
}
