using System;
using Content.Server._HL.Weapons.Components;
using Content.Server.Stunnable;
using Content.Shared.Mobs.Components;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Events;

namespace Content.Server._HL.Weapons.Systems;

public sealed class GunUserThrowbackSystem : EntitySystem
{
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly ThrownItemSystem _thrownItems = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RecoilImpactStunComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<RecoilImpactStunComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<RecoilImpactStunComponent, StopThrowEvent>(OnStopThrow);
    }

    private void OnStartCollide(Entity<RecoilImpactStunComponent> ent, ref StartCollideEvent args)
    {
        if (!TryComp(ent, out ThrownItemComponent? thrown))
        {
            RemCompDeferred<RecoilImpactStunComponent>(ent);
            return;
        }

        if (!args.OtherFixture.Hard || HasComp<MobStateComponent>(args.OtherEntity))
            return;

        _thrownItems.StopThrow(ent, thrown);
        _stun.TryStun(ent, TimeSpan.FromSeconds(ent.Comp.StunTime), true);
    }

    private void OnLand(Entity<RecoilImpactStunComponent> ent, ref LandEvent args)
    {
        RemCompDeferred<RecoilImpactStunComponent>(ent);
    }

    private void OnStopThrow(EntityUid uid, RecoilImpactStunComponent component, StopThrowEvent args)
    {
        RemCompDeferred<RecoilImpactStunComponent>(uid);
    }
}