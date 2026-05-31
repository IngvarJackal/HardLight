using Content.Shared.Abilities;
using Content.Shared._DV.CCVars;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client.Nyanotrasen.Overlays;

public sealed partial class DogVisionSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ISharedPlayerManager _playerMan = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private DogVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DogVisionComponent, ComponentInit>(OnDogVisionInit);
        SubscribeLocalEvent<DogVisionComponent, ComponentShutdown>(OnDogVisionShutdown);
        SubscribeLocalEvent<DogVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<DogVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        // No-bypass variants: always apply overlay regardless of accessibility cvar.
        SubscribeLocalEvent<DogVisionNoBypassComponent, ComponentInit>(OnDogVisionNoBypassInit);
        SubscribeLocalEvent<DogVisionNoBypassComponent, ComponentShutdown>(OnDogVisionNoBypassShutdown);
        SubscribeLocalEvent<DogVisionNoBypassComponent, LocalPlayerAttachedEvent>(OnDogVisionNoBypassPlayerAttached);
        SubscribeLocalEvent<DogVisionNoBypassComponent, LocalPlayerDetachedEvent>(OnDogVisionNoBypassPlayerDetached);

        Subs.CVar(_cfg, DCCVars.NoVisionFilters, OnNoVisionFiltersChanged);

        _overlay = new();
    }

    private void OnDogVisionInit(EntityUid uid, DogVisionComponent component, ComponentInit args)
    {
        if (uid == _playerMan.LocalEntity && !_cfg.GetCVar(DCCVars.NoVisionFilters))
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnDogVisionShutdown(EntityUid uid, DogVisionComponent component, ComponentShutdown args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(EntityUid uid, DogVisionComponent component, LocalPlayerAttachedEvent args)
    {
        if (!_cfg.GetCVar(DCCVars.NoVisionFilters))
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(EntityUid uid, DogVisionComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnNoVisionFiltersChanged(bool enabled)
    {
        // If a no-bypass component exists on the local player, ignore the accessibility toggle.
        var local = _playerMan.LocalEntity;
        var hasNoBypass = local is { Valid: true } && _entityManager.HasComponent<DogVisionNoBypassComponent>(local.Value);

        if (enabled && !hasNoBypass)
            _overlayMan.RemoveOverlay(_overlay);
        else
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnDogVisionNoBypassInit(EntityUid uid, DogVisionNoBypassComponent component, ComponentInit args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnDogVisionNoBypassShutdown(EntityUid uid, DogVisionNoBypassComponent component, ComponentShutdown args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnDogVisionNoBypassPlayerAttached(EntityUid uid, DogVisionNoBypassComponent component, LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_overlay);
    }

    private void OnDogVisionNoBypassPlayerDetached(EntityUid uid, DogVisionNoBypassComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
    }
}
