using System;
using Content.Shared._HL.Weapons.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._HL.Weapons.Systems;

public sealed class SharedGunUserThrowbackSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(ref GunShotEvent args)
    {
        var shooter = args.User;
        if (!TryComp<GunUserThrowbackComponent>(shooter, out var throwback))
            return;

        if (!TryComp<PhysicsComponent>(shooter, out var physics))
            return;

        if (throwback.Strength <= 0f)
            return;

        var shotPayload = GetShotPayload(args, throwback);
        if (shotPayload <= 0f)
            return;

        var recoilStrength = throwback.Strength * shotPayload * GetMassFactor(shooter, throwback);
        var wielded = TryComp<WieldableComponent>(args.Gun, out var wieldable) && wieldable.Wielded;

        if (wielded)
            recoilStrength *= throwback.WieldedMultiplier;

        recoilStrength *= throwback.ImpulseMultiplier;

        if (recoilStrength <= 0f)
            return;

        var fromCoordinates = _transform.ToMapCoordinates(args.FromCoordinates);
        var targetCoordinates = _transform.ToMapCoordinates(args.ToCoordinates);

        if (fromCoordinates.MapId != targetCoordinates.MapId)
            return;

        var recoilDirection = fromCoordinates.Position - targetCoordinates.Position;
        if (recoilDirection.LengthSquared() <= 0f)
            return;

        _physics.ApplyLinearImpulse(shooter, recoilDirection.Normalized() * recoilStrength, body: physics);

        var applied = new GunUserThrowbackAppliedEvent(args.Gun, wielded, throwback.CollisionStunTime);
        RaiseLocalEvent(shooter, ref applied);
    }

    private float GetShotPayload(GunShotEvent args, GunUserThrowbackComponent throwback)
    {
        var totalDamage = 0f;
        var projectileCount = 0;

        foreach (var shot in args.Ammo)
        {
            projectileCount++;
            totalDamage += GetAmmoDamage(shot.Shootable);
        }

        if (projectileCount == 0)
            return 0f;

        var basePayload = projectileCount * throwback.ProjectileMultiplier + totalDamage * throwback.DamageMultiplier;
        var volleyFactor = 1f + Math.Max(0, projectileCount - 1) * throwback.VolleyMultiplier;
        return basePayload * volleyFactor;
    }

    private float GetAmmoDamage(object shootable)
    {
        switch (shootable)
        {
            case ProjectileComponent projectile:
                return projectile.Damage.GetTotal().Float();
            case CartridgeAmmoComponent cartridge when _prototypeManager.TryIndex<EntityPrototype>(cartridge.Prototype, out var prototype)
                                                    && prototype.TryGetComponent<ProjectileComponent>(out var projectile, _componentFactory):
                return projectile.Damage.GetTotal().Float();
            default:
                return 0f;
        }
    }

    private float GetMassFactor(EntityUid shooter, GunUserThrowbackComponent throwback)
    {
        if (!TryComp<PhysicsComponent>(shooter, out var physics) || physics.Mass <= 0f)
            return 1f;

        var factor = throwback.ReferenceMass / physics.Mass;
        return Math.Clamp(factor, throwback.MinMassFactor, throwback.MaxMassFactor);
    }
}

[ByRefEvent]
public record struct GunUserThrowbackAppliedEvent(EntityUid Gun, bool Wielded, float CollisionStunTime);