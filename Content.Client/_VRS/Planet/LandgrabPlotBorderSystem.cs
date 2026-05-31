using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Content.Client._VRS.Planet;

/// <summary>
/// Manages the lifetime of <see cref="LandgrabPlotBorderOverlay"/>.
/// The overlay is added when a local player session exists and removed on detach.
/// </summary>
public sealed class LandgrabPlotBorderSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private LandgrabPlotBorderOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);

        // If a player is already attached when this system initialises (hot
        // reload, late system load), AttachedEvent has already fired — register
        // the overlay immediately so the user isn't stuck without it.
        if (_player.LocalSession?.AttachedEntity != null)
        {
            EnsureOverlay();
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        RemoveOverlay();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        EnsureOverlay();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        RemoveOverlay();
    }

    private void EnsureOverlay()
    {
        if (_overlays.HasOverlay<LandgrabPlotBorderOverlay>())
            return;

        _overlay = new LandgrabPlotBorderOverlay();
        _overlays.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlay != null)
        {
            _overlays.RemoveOverlay(_overlay);
            _overlay = null;
        }
    }
}
