using Content.Shared.Abilities;
using Content.Shared._DV.CCVars;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._DV.Overlays;

public sealed partial class UltraVisionSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ISharedPlayerManager _playerMan = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private UltraVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UltraVisionComponent, ComponentInit>(OnUltraVisionInit);
        SubscribeLocalEvent<UltraVisionComponent, ComponentShutdown>(OnUltraVisionShutdown);
        SubscribeLocalEvent<UltraVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<UltraVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        // No-bypass variants: always apply overlay regardless of accessibility cvar.
        SubscribeLocalEvent<UltraVisionNoBypassComponent, ComponentInit>(OnUltraVisionNoBypassInit);
        SubscribeLocalEvent<UltraVisionNoBypassComponent, ComponentShutdown>(OnUltraVisionNoBypassShutdown);
        SubscribeLocalEvent<UltraVisionNoBypassComponent, LocalPlayerAttachedEvent>(OnUltraVisionNoBypassPlayerAttached);
        SubscribeLocalEvent<UltraVisionNoBypassComponent, LocalPlayerDetachedEvent>(OnUltraVisionNoBypassPlayerDetached);

        Subs.CVar(_cfg, DCCVars.NoVisionFilters, OnNoVisionFiltersChanged);

        _overlay = new();
    }

    private void OnUltraVisionInit(EntityUid uid, UltraVisionComponent component, ComponentInit args)
    {
        if (uid == _playerMan.LocalEntity && !_cfg.GetCVar(DCCVars.NoVisionFilters))
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnUltraVisionShutdown(EntityUid uid, UltraVisionComponent component, ComponentShutdown args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(EntityUid uid, UltraVisionComponent component, LocalPlayerAttachedEvent args)
    {
        if (!_cfg.GetCVar(DCCVars.NoVisionFilters))
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(EntityUid uid, UltraVisionComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnNoVisionFiltersChanged(bool enabled)
    {
        var local = _playerMan.LocalEntity;
        var hasNoBypass = local is { Valid: true } && _entityManager.HasComponent<UltraVisionNoBypassComponent>(local.Value);

        if (enabled && !hasNoBypass)
            _overlayMan.RemoveOverlay(_overlay);
        else
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnUltraVisionNoBypassInit(EntityUid uid, UltraVisionNoBypassComponent component, ComponentInit args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnUltraVisionNoBypassShutdown(EntityUid uid, UltraVisionNoBypassComponent component, ComponentShutdown args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnUltraVisionNoBypassPlayerAttached(EntityUid uid, UltraVisionNoBypassComponent component, LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_overlay);
    }

    private void OnUltraVisionNoBypassPlayerDetached(EntityUid uid, UltraVisionNoBypassComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
    }
}
