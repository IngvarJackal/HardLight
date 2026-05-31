using Content.Client.Movement.Components;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Client.Movement.Systems;

/// <summary>
///     Toggles <see cref="EyeCursorOffsetComponent.Enabled"/> on the active held weapon
///     when the player right-clicks while in combat mode.
/// </summary>
public sealed class ScopeToggleSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(EngineKeyFunctions.UseSecondary, new ScopeToggleHandler(this))
            .Register<ScopeToggleSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<ScopeToggleSystem>();
    }

    private bool TryToggleScope()
    {
        var player = _player.LocalEntity;
        if (player == null)
            return false;

        if (!_combatMode.IsInCombatMode(player.Value))
            return false;

        if (!TryComp<HandsComponent>(player.Value, out var hands))
            return false;

        if (hands.ActiveHandEntity is not { } weaponUid)
            return false;

        if (!TryComp<EyeCursorOffsetComponent>(weaponUid, out var eyeOffset))
            return false;

        eyeOffset.Enabled = !eyeOffset.Enabled;

        if (!eyeOffset.Enabled)
            eyeOffset.CurrentPosition = System.Numerics.Vector2.Zero;

        return true;
    }

    private sealed class ScopeToggleHandler : InputCmdHandler
    {
        private readonly ScopeToggleSystem _sys;

        public ScopeToggleHandler(ScopeToggleSystem sys)
        {
            _sys = sys;
        }

        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            if (message.State != BoundKeyState.Down)
                return false;

            return _sys.TryToggleScope();
        }
    }
}
