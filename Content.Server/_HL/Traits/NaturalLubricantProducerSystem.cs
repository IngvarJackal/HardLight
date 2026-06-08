using Content.Server.Fluids.EntitySystems;
using Content.Server.Popups;
using Content.Shared._HL.Traits.Events;
using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._HL.Traits;

/// <summary>
/// HardLight: handles the <see cref="NaturalLubricantProducerComponent"/> - the toggleable
/// leaking action and the silent floor leak timer. Idle (non-leaking) producers cost only a
/// single boolean check per tick.
/// </summary>
public sealed class NaturalLubricantProducerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NaturalLubricantProducerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NaturalLubricantProducerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NaturalLubricantProducerComponent, ToggleLeakingActionEvent>(OnToggleLeaking);
    }

    private void OnStartup(Entity<NaturalLubricantProducerComponent> ent, ref ComponentStartup args)
    {
        // Grant the toggleable leaking ability as an action rather than a verb.
        _actions.AddAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
    }

    private void OnShutdown(Entity<NaturalLubricantProducerComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ToggleActionEntity);
    }

    private void OnToggleLeaking(Entity<NaturalLubricantProducerComponent> ent, ref ToggleLeakingActionEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        ent.Comp.Leaking = !ent.Comp.Leaking;
        if (ent.Comp.Leaking)
            ent.Comp.NextLeak = _timing.CurTime + NextLeakDelay(ent.Comp);

        _popup.PopupEntity(
            Loc.GetString(ent.Comp.Leaking ? "lubricant-leak-toggle-on" : "lubricant-leak-toggle-off"),
            ent.Owner,
            ent.Owner);
    }

    private TimeSpan NextLeakDelay(NaturalLubricantProducerComponent comp)
    {
        return TimeSpan.FromSeconds(_random.NextFloat(comp.LeakIntervalMin, comp.LeakIntervalMax));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NaturalLubricantProducerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Idle producers (not leaking, or leak not yet due) cost only this check.
            if (!comp.Leaking || now < comp.NextLeak)
                continue;

            comp.NextLeak = now + NextLeakDelay(comp);

            if (_mobState.IsDead(uid))
                continue;

            // Silently dump a fresh splash of lubricant onto the floor.
            _puddle.TrySpillAt(uid, new Solution(comp.ReagentId, comp.Volume), out _, sound: false);
        }
    }
}
