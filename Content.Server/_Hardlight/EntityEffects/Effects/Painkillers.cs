using Content.Server.EntityEffects.Effects.StatusEffects;
using Content.Shared.EntityEffects;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class Painkillers : EntityEffect
{
    [DataField]
    public StatusEffectMetabolismType Type = StatusEffectMetabolismType.Add;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        switch (Type)
        {
            case StatusEffectMetabolismType.Set:
            case StatusEffectMetabolismType.Add:
            {
                return Loc.GetString("reagent-effect-guidebook-painkillers");
            }

            case StatusEffectMetabolismType.Remove:
            {
                return Loc.GetString("reagent-effect-guidebook-painkillers-remove");
            }
        }

        return "This should never happen!";
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        var manager = args.EntityManager;
        switch (Type)
        {
            case StatusEffectMetabolismType.Set:
            case StatusEffectMetabolismType.Add:
            {
                if (!manager.HasComponent<PainNumbnessComponent>(args.TargetEntity))
                {
                    manager.AddComponent<PainNumbnessComponent>(args.TargetEntity);
                }

                break;
            }

            case StatusEffectMetabolismType.Remove:
            {
                if (manager.TryGetComponent<PainNumbnessComponent>(args.TargetEntity, out var numbness))
                {
                    if (!numbness.Permanent)
                    {
                        manager.RemoveComponent<PainNumbnessComponent>(args.TargetEntity);
                    }
                }

                break;
            }
        }
    }
}
