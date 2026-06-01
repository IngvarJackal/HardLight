using System.Numerics;
using Content.Shared.GameTicking;
using Content.Shared._Mono.Radar;
using Content.Shared.HL.CCVar;
using NFRadarBlipShape = Content.Shared._NF.Radar.RadarBlipShape;
using Content.Shared.Shuttles.Components;
using RadarBlipComponent = Content.Server._NF.Radar.RadarBlipComponent;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Radar;

public sealed partial class RadarBlipSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly HitscanRadarSystem _hitscanRadar = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly Dictionary<NetUserId, TimeSpan> _nextBlipRequestPerUser = new();
    private readonly Dictionary<EntityUid, CachedRadarReport> _recentRadarReports = new();

    // Pooled collections to avoid per-request heap churn
    private readonly List<BlipNetData> _tempBlipsCache = new();
    private readonly List<HitscanNetData> _tempHitscansCache = new();
    private readonly List<EntityUid> _tempSourcesCache = new();
    private readonly List<MapCoordinates> _tempSourceMapCoordinatesCache = new();
    private readonly List<Vector2> _tempSourcePositionsCache = new();
    private readonly HashSet<EntityUid> _tempSourceGridsCache = new();
    private readonly List<BlipConfig> _tempPaletteCache = new();
    private readonly Dictionary<BlipConfig, ushort> _paletteIndex = new();
    private bool _hasGridlessSource;

    private static readonly TimeSpan MinRequestPeriod = TimeSpan.FromMilliseconds(225);
    private static readonly TimeSpan ReportCacheLifetime = TimeSpan.FromMilliseconds(75);

    // HardLight: live-tunable overrides via CVars (HLCCVars.RadarMinRequestMs / RadarReportCacheTtlMs).
    // Defaults below are fallbacks if the CVars are not yet bound at first request.
    private TimeSpan _minRequestPeriod = MinRequestPeriod;
    private TimeSpan _reportCacheLifetime = ReportCacheLifetime;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestBlipsEvent>(OnBlipsRequested);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<RadarBlipComponent, ComponentShutdown>(OnBlipShutdown);

        Subs.CVar(_cfg, HLCCVars.RadarMinRequestMs,
            v => _minRequestPeriod = TimeSpan.FromMilliseconds(Math.Max(0, v)), true);
        Subs.CVar(_cfg, HLCCVars.RadarReportCacheTtlMs,
            v => _reportCacheLifetime = TimeSpan.FromMilliseconds(Math.Max(0, v)), true);
    }

    private void OnBlipsRequested(RequestBlipsEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetEntity(ev.Radar, out var radarUid)
            || !TryComp<RadarConsoleComponent>(radarUid, out var radar)
        )
            return;

        var now = _timing.RealTime;
        if (_nextBlipRequestPerUser.TryGetValue(args.SenderSession.UserId, out var requestTime) && now < requestTime)
            return;

        _nextBlipRequestPerUser[args.SenderSession.UserId] = now + _minRequestPeriod;

        PrepareRadarSources(radarUid.Value);

        if (_recentRadarReports.TryGetValue(radarUid.Value, out var cachedReport)
            && now - cachedReport.CreatedAt <= _reportCacheLifetime)
        {
            _hitscanRadar.CopyVisibleHitscans(_tempSourcePositionsCache, radar.MaxRange, _tempHitscansCache);
            RaiseNetworkEvent(new GiveBlipsEvent(cachedReport.ConfigPalette, cachedReport.Blips, new List<HitscanNetData>(_tempHitscansCache)), args.SenderSession);
            ClearTemporaryState();
            return;
        }

        AssembleBlipsReport((EntityUid)radarUid, _tempSourcePositionsCache, radar);
        _hitscanRadar.CopyVisibleHitscans(_tempSourcePositionsCache, radar.MaxRange, _tempHitscansCache);

        var report = new CachedRadarReport(
            now,
            new List<BlipConfig>(_tempPaletteCache),
            new List<BlipNetData>(_tempBlipsCache));
        _recentRadarReports[radarUid.Value] = report;

        // Combine the blips and hitscan lines
        var giveEv = new GiveBlipsEvent(report.ConfigPalette, report.Blips, new List<HitscanNetData>(_tempHitscansCache));
        RaiseNetworkEvent(giveEv, args.SenderSession);

        ClearTemporaryState();
    }

    private void PrepareRadarSources(EntityUid radarUid)
    {
        var sourcesEv = new GetRadarSourcesEvent();
        RaiseLocalEvent(radarUid, ref sourcesEv);

        _tempSourcesCache.Clear();
        if (sourcesEv.Sources != null)
            _tempSourcesCache.AddRange(sourcesEv.Sources);
        else
            _tempSourcesCache.Add(radarUid);

        _tempSourcePositionsCache.Clear();
        _tempSourceMapCoordinatesCache.Clear();
        _tempSourceGridsCache.Clear();
        _hasGridlessSource = false;
        var radarMapId = Transform(radarUid).MapID;

        foreach (var source in _tempSourcesCache)
        {
            if (TerminatingOrDeleted(source))
                continue;

            var sourceMap = _xform.GetMapCoordinates(source);
            if (sourceMap.MapId != radarMapId)
                continue;

            if (Transform(source).GridUid is { } sourceGrid)
                _tempSourceGridsCache.Add(sourceGrid);
            else
                _hasGridlessSource = true;

            _tempSourceMapCoordinatesCache.Add(sourceMap);
            _tempSourcePositionsCache.Add(_xform.GetWorldPosition(source));
        }
    }

    private void ClearTemporaryState()
    {
        _tempBlipsCache.Clear();
        _tempHitscansCache.Clear();
        _tempSourcesCache.Clear();
        _tempSourceMapCoordinatesCache.Clear();
        _tempSourcePositionsCache.Clear();
        _tempSourceGridsCache.Clear();
        _hasGridlessSource = false;
        _tempPaletteCache.Clear();
        _paletteIndex.Clear();
    }

    private void OnBlipShutdown(EntityUid blipUid, RadarBlipComponent component, ComponentShutdown args)
    {
        var netBlipUid = GetNetEntity(blipUid);

        // Surgically remove this blip from any cached reports rather than clearing the
        // entire _recentRadarReports dictionary on every shutdown. The previous behaviour
        // thrashed the cache during combat (every projectile/torpedo despawn invalidated
        // every console's cache), forcing a full AssembleBlipsReport per console per frame.
        // Per-blip removal preserves cache hits for unrelated blips while still preventing
        // a dead blip from appearing in a cached payload served within the 75ms TTL.
        foreach (var report in _recentRadarReports.Values)
        {
            var blips = report.Blips;
            for (var i = blips.Count - 1; i >= 0; i--)
            {
                if (blips[i].Uid == netBlipUid)
                {
                    blips.RemoveAt(i);
                    break;
                }
            }
        }

        var removalEv = new BlipRemovalEvent(netBlipUid);
        RaiseNetworkEvent(removalEv);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _nextBlipRequestPerUser.Clear();
        _recentRadarReports.Clear();
        _tempSourceGridsCache.Clear();
        _hasGridlessSource = false;
    }

    private void AssembleBlipsReport(EntityUid uid, List<Vector2> sourcePositions, RadarConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var radarXform = Transform(uid);
        var radarMapId = radarXform.MapID;

        // Walk only entities tagged with RadarBlipComponent rather than gathering every entity
        // in radar range via broadphase. Blip count is small (projectiles + a few tagged things);
        // broadphase cost scaled with grids × fixtures, which made server tick time grow with
        // the number of ships loaded into a map regardless of how many were actually emitting blips.
        var blipEnumerator = EntityQueryEnumerator<RadarBlipComponent, TransformComponent, PhysicsComponent>();
        while (blipEnumerator.MoveNext(out var blipUid, out var blip, out var blipXform, out var blipPhysics))
        {
            if (!blip.Enabled
                || blipXform.MapID != radarMapId
                || !NearAnySources(_xform.GetWorldPosition(blipXform), sourcePositions, component.MaxRange)
            )
                continue;

            var blipGrid = blipXform.GridUid;

            if (blip.RequireNoGrid && blipGrid != null // if we want no grid but we are on a grid
                || !blip.VisibleFromOtherGrids && !MatchesAnyRadarSourceGrid(blipGrid)
            )
                continue; // don't show this blip

            var netBlipUid = GetNetEntity(blipUid);

            var blipVelocity = _physics.GetMapLinearVelocity(blipUid, blipPhysics, blipXform);

            // due to PVS being a thing, things will break if we try to parent to not the map or a grid
            var coord = blipXform.Coordinates;
            if (blipXform.ParentUid != blipXform.MapUid && blipXform.ParentUid != blipGrid)
                coord = _xform.WithEntityId(coord, blipGrid ?? blipXform.MapUid!.Value);

            var shape = blip.Shape switch
            {
                NFRadarBlipShape.Circle => RadarBlipShape.Circle,
                NFRadarBlipShape.Square => RadarBlipShape.Square,
                NFRadarBlipShape.Triangle => RadarBlipShape.Triangle,
                NFRadarBlipShape.Star => RadarBlipShape.Star,
                NFRadarBlipShape.Diamond => RadarBlipShape.Diamond,
                NFRadarBlipShape.Hexagon => RadarBlipShape.Hexagon,
                NFRadarBlipShape.Arrow => RadarBlipShape.Arrow,
                _ => RadarBlipShape.Circle,
            };

            var config = new BlipConfig
            {
                Color = blip.RadarColor,
                Shape = shape,
                Bounds = new Box2(-blip.Scale * 1.5f, -blip.Scale * 1.5f, blip.Scale * 1.5f, blip.Scale * 1.5f)
            };

            BlipConfig? gridCfg = null;
            var rotation = _xform.GetWorldRotation(blipXform);

            // we're parented to either the map or a grid and this is relative velocity so account for grid movement
            if (blipGrid != null)
            {
                var gridXform = Transform(blipGrid.Value);
                blipVelocity -= _physics.GetLinearVelocity(blipGrid.Value, coord.Position);
                // it's local-frame velocity so rotate it too
                blipVelocity = (-gridXform.LocalRotation).RotateVec(blipVelocity);
                // and also offset the rotation
                rotation -= gridXform.LocalRotation;
            }

            var configIdx = GetOrAddConfig(config);
            ushort? gridConfigIdx = gridCfg is { } gridCf ? GetOrAddConfig(gridCf) : null;

            // ideally we would handle blips being culled by detection on server but detection grid culling is already clientside so might as well
            _tempBlipsCache.Add(new(netBlipUid,
                            GetNetCoordinates(coord),
                            blipVelocity,
                            rotation,
                            configIdx,
                            gridConfigIdx));
        }
    }

    /// <summary>
    /// Gets or create palette index for blip config.
    /// </summary>
    private ushort GetOrAddConfig(BlipConfig config)
    {
        if (_paletteIndex.TryGetValue(config, out var index))
            return index;

        if (_tempPaletteCache.Count >= ushort.MaxValue)
        {
            Log.Error($"Blip config count overflow! Reached max {ushort.MaxValue}, but trying to add more.");
            return 0;
        }

        index = (ushort)_tempPaletteCache.Count;
        _tempPaletteCache.Add(config);
        _paletteIndex[config] = index;
        return index;
    }

    private bool NearAnySources(Vector2 coord, List<Vector2> sourcePositions, float range)
    {
        var rsqr = range * range;
        foreach (var sourcePosition in sourcePositions)
        {
            if ((sourcePosition - coord).LengthSquared() < rsqr)
                return true;
        }

        return false;
    }

    private bool MatchesAnyRadarSourceGrid(EntityUid? blipGrid)
    {
        if (blipGrid == null)
            return _hasGridlessSource;

        return _tempSourceGridsCache.Contains(blipGrid.Value);
    }

    private sealed record CachedRadarReport(
        TimeSpan CreatedAt,
        List<BlipConfig> ConfigPalette,
        List<BlipNetData> Blips);
}
