using Content.Server.Explosion.EntitySystems;
using Content.Server.Radiation.Components;
using Content.Server.Radiation.Events;
using Content.Shared.Radiation.Components;

namespace Content.Server._Mono.Radiation;

/// <summary>
/// Cascading-radiation reactor: as the entity receives radiation it emits more,
/// and once a threshold is exceeded it explodes (and tries to drag any nearby
/// chain-radiation entities into the same explosion). Ported from Mono.
/// </summary>
public sealed class ChainRadiationSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadiationSystemUpdatedEvent>(OnUpdate);
    }

    private void OnUpdate(RadiationSystemUpdatedEvent args)
    {
        var query = EntityQueryEnumerator<ChainRadiationComponent, RadiationReceiverComponent, RadiationSourceComponent>();
        while (query.MoveNext(out var uid, out var chain, out var receiver, out var source))
        {
            source.Intensity = chain.BaseIntensity * (1f + receiver.CurrentRadiation * chain.Coefficient);

            // not great not terrible
            if (source.Intensity > chain.ExplosionThreshold)
            {
                var coord = Transform(uid).Coordinates;
                foreach (var other in _lookup.GetEntitiesInRange<ChainRadiationComponent>(coord, chain.ChainExplosionRadius))
                {
                    // teleport them to us for combined explosion
                    _transform.SetCoordinates(other, coord);

                    Explode(other);
                }

                Explode((uid, chain));
            }
        }
    }

    private void Explode(Entity<ChainRadiationComponent> ent)
    {
        // VRS: ExplosiveComponent has been locked to SharedExplosionSystem access, so
        // mutate parameters via QueueExplosion directly instead of writing to the
        // component as Mono originally did.
        _explosion.QueueExplosion(
            ent,
            ent.Comp.ExplosionType,
            ent.Comp.TotalIntensity,
            ent.Comp.IntensitySlope,
            ent.Comp.MaxIntensity);

        QueueDel(ent.Owner);
    }
}
