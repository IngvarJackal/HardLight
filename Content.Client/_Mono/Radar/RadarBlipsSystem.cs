using System.Numerics;
using Content.Shared._Mono.Radar;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Client._Mono.Radar;

public sealed partial class RadarBlipsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _map = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private const double BlipStaleSeconds = 3.0;
    private static readonly List<(NetEntity? Grid, Vector2 Start, Vector2 End, float Thickness, Color Color)> EmptyHitscanList = new();
    private TimeSpan _lastRequestTime = TimeSpan.Zero;
    public static readonly TimeSpan RequestThrottle = TimeSpan.FromMilliseconds(225);

    private TimeSpan _lastUpdatedTime;
    private TimeSpan _lastHitscanUpdatedTime;
    private List<BlipNetData> _blips = new();
    private List<HitscanNetData> _hitscans = new();
    private List<BlipConfig> _configPalette = new();
    private Vector2 _radarWorldPosition;
    private float _radarRenderDistanceSq = float.MaxValue;

    // cached results to avoid allocating on every draw/frame
    private readonly List<BlipData> _cachedBlipData = new();
    private readonly List<Vector2> _renderSourcePositions = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GiveBlipsEvent>(HandleReceiveBlips);
        SubscribeNetworkEvent<BlipRemovalEvent>(RemoveBlip);
    }

    private void HandleReceiveBlips(GiveBlipsEvent ev, EntitySessionEventArgs args)
    {
        _configPalette = ev.ConfigPalette;
        _blips = ev.Blips;
        _hitscans = ev.HitscanLines;
        _lastUpdatedTime = _timing.CurTime;
        _lastHitscanUpdatedTime = _timing.CurTime;

        // Debug: which blip net ids did the client actually receive this update? Correlate with the
        // server's radar.blip.include / .skip lines to see where shells stop being sent / rendered.
        var ids = string.Join(",", ev.Blips.Select(b => b.Uid.Id));
        Content.Shared._Mono.Debugging.ProjDebug.Log("radar.blip.recv",
            $"count={ev.Blips.Count} hitscans={ev.HitscanLines.Count} netIds=[{ids}]");
    }

    private void RemoveBlip(BlipRemovalEvent args)
    {
        var blipid = _blips.FirstOrDefault(x => x.Uid == args.NetBlipUid);
        _blips.Remove(blipid);
    }

    public void RequestBlips(EntityUid console, bool force = false)
    {
        // Only request if we have a valid console
        if (!Exists(console))
            return;

        // Add request throttling to avoid network spam
        if (!force && _timing.CurTime - _lastRequestTime < RequestThrottle)
            return;

        _lastRequestTime = _timing.CurTime;

        // Cache the radar position for distance culling
        _radarWorldPosition = _xform.GetWorldPosition(console);
        _radarRenderDistanceSq = TryComp<RadarConsoleComponent>(console, out var radar)
            ? radar.MaxRange * radar.MaxRange
            : float.MaxValue;
        var netConsole = GetNetEntity(console);
        var ev = new RequestBlipsEvent(netConsole);
        RaiseNetworkEvent(ev);
    }

    /// <summary>
    /// Gets the current blips as world positions with their scale, color and shape.
    /// </summary>
    public List<BlipData> GetCurrentBlips()
    {
        // clear the cache and bail early if the data is stale
        _cachedBlipData.Clear();
        if (_timing.CurTime.TotalSeconds - _lastUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return _cachedBlipData;

        // populate the cached list instead of allocating a new one each frame
        foreach (var blip in _blips)
        {
            var coord = GetCoordinates(blip.Position);

            if (!coord.IsValid(EntityManager))
                continue;

            var predictedPos = new EntityCoordinates(coord.EntityId, coord.Position + blip.Vel * (float)(_timing.CurTime - _lastUpdatedTime).TotalSeconds);

            var predictedMap = _xform.ToMapCoordinates(predictedPos);

            var config = _configPalette[blip.ConfigIndex];
            var rotation = blip.Rotation;
            // hijack our shape if we're on a grid and we want to do that
            if (_map.TryFindGridAt(predictedMap, out var grid, out _) && grid != EntityUid.Invalid)
            {
                if (blip.OnGridConfigIndex is { } gridIdx)
                    config = _configPalette[gridIdx];
                rotation += Transform(grid).LocalRotation;
            }
            var maybeGrid = grid != EntityUid.Invalid ? grid : (EntityUid?)null;

            // HardLight: client-side distance fade. Server only tags the blip's fade mode; the alpha
            // curve lives here. Enemy shells fade out near the 512 m edge; own shells stay bright out
            // to ~2 km. Distance is from the requesting radar console.
            var distance = (predictedMap.Position - _radarWorldPosition).Length();
            var alpha = BlipAlpha(blip.Fade, distance);

            _cachedBlipData.Add(new(blip.Uid, predictedPos, rotation, maybeGrid, config, alpha));
        }

        return _cachedBlipData;
    }

    /// <summary>
    /// HardLight: per-blip render alpha as a function of distance from the radar. Both projectile
    /// curves drop 25% every 32 m and reach 100% below the ramp start:
    /// enemy shells 75%@448 / 50%@480 / 25%@512; own shells 75%@1952 / 50%@1984 / 25%@2016.
    /// Non-projectile blips keep the previous constant alpha.
    /// </summary>
    private static float BlipAlpha(RadarBlipFadeMode fade, float distance)
    {
        return fade switch
        {
            RadarBlipFadeMode.OtherProjectile => Math.Clamp(1f - MathF.Max(0f, distance - 416f) / 32f * 0.25f, 0f, 1f),
            RadarBlipFadeMode.OwnProjectile => Math.Clamp(1f - MathF.Max(0f, distance - 1920f) / 32f * 0.25f, 0f, 1f),
            _ => 0.8f,
        };
    }

    /// <summary>
    /// Gets the hitscan lines to be rendered on the radar
    /// </summary>
    public List<HitscanNetData> GetHitscanLines()
    {
        if (_timing.CurTime.TotalSeconds - _lastHitscanUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return new();

        return _hitscans;
    }

    /// <summary>
    /// Gets the hitscan lines to be rendered on the radar
    /// </summary>
    public List<(Vector2 Start, Vector2 End, float Thickness, Color Color)> GetWorldHitscanLines(IReadOnlyList<EntityUid>? sourceEntities = null)
    {
        if (_timing.CurTime.TotalSeconds - _lastHitscanUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return new List<(Vector2, Vector2, float, Color)>();

        var result = new List<(Vector2, Vector2, float, Color)>(_hitscans.Count);
        var sourcePositions = GetRenderSourcePositions(sourceEntities);

        foreach (var hitscan in _hitscans)
        {
            Vector2 worldStart, worldEnd;

            // If no grid, positions are already in world coordinates
            if (hitscan.Grid == null)
            {
                worldStart = hitscan.Start;
                worldEnd = hitscan.End;

                // Distance culling - check if either end of the line is in range
                if (!IsLineInRenderRange(worldStart, worldEnd, sourcePositions))
                    continue;

                result.Add((worldStart, worldEnd, hitscan.Thickness, hitscan.Color));
                continue;
            }

            // If grid exists, transform from grid-local to world coordinates
            if (TryGetEntity(hitscan.Grid, out var gridEntity))
            {
                // Transform the grid-local positions to world positions
                var worldPos = _xform.GetWorldPosition(gridEntity.Value);
                var gridRot = _xform.GetWorldRotation(gridEntity.Value);

                // Rotate the local positions by grid rotation and add grid position
                var rotatedLocalStart = gridRot.RotateVec(hitscan.Start);
                var rotatedLocalEnd = gridRot.RotateVec(hitscan.End);

                worldStart = worldPos + rotatedLocalStart;
                worldEnd = worldPos + rotatedLocalEnd;

                // Distance culling - check if either end of the line is in range
                if (!IsLineInRenderRange(worldStart, worldEnd, sourcePositions))
                    continue;

                result.Add((worldStart, worldEnd, hitscan.Thickness, hitscan.Color));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the raw hitscan data which includes grid information for more accurate rendering.
    /// </summary>
    public List<(NetEntity? Grid, Vector2 Start, Vector2 End, float Thickness, Color Color)> GetRawHitscanLines(IReadOnlyList<EntityUid>? sourceEntities = null)
    {
        if (_timing.CurTime.TotalSeconds - _lastHitscanUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return EmptyHitscanList;

        if (_hitscans.Count == 0)
            return EmptyHitscanList;

        var filteredHitscans = new List<(NetEntity? Grid, Vector2 Start, Vector2 End, float Thickness, Color Color)>(_hitscans.Count);
        var sourcePositions = GetRenderSourcePositions(sourceEntities);

        foreach (var hitscan in _hitscans)
        {
            // For non-grid hitscans, do direct distance check
            if (hitscan.Grid == null)
            {
                if (IsLineInRenderRange(hitscan.Start, hitscan.End, sourcePositions))
                {
                    filteredHitscans.Add((hitscan.Grid, hitscan.Start, hitscan.End, hitscan.Thickness, hitscan.Color));
                }
                continue;
            }

            // For grid hitscans, transform to world space for distance check
            if (TryGetEntity(hitscan.Grid, out var gridEntity))
            {
                var worldPos = _xform.GetWorldPosition(gridEntity.Value);
                var gridRot = _xform.GetWorldRotation(gridEntity.Value);

                var rotatedLocalStart = gridRot.RotateVec(hitscan.Start);
                var rotatedLocalEnd = gridRot.RotateVec(hitscan.End);

                var worldStart = worldPos + rotatedLocalStart;
                var worldEnd = worldPos + rotatedLocalEnd;

                if (IsLineInRenderRange(worldStart, worldEnd, sourcePositions))
                {
                    filteredHitscans.Add((hitscan.Grid, hitscan.Start, hitscan.End, hitscan.Thickness, hitscan.Color));
                }
            }
        }

        return filteredHitscans;
    }

    private List<Vector2> GetRenderSourcePositions(IReadOnlyList<EntityUid>? sourceEntities)
    {
        _renderSourcePositions.Clear();

        if (sourceEntities != null)
        {
            foreach (var source in sourceEntities)
            {
                if (Exists(source))
                    _renderSourcePositions.Add(_xform.GetWorldPosition(source));
            }
        }

        if (_renderSourcePositions.Count == 0)
            _renderSourcePositions.Add(_radarWorldPosition);

        return _renderSourcePositions;
    }

    // Mono: cull against segment distance so long beams crossing the radar range still render.
    private bool IsLineInRenderRange(Vector2 start, Vector2 end, List<Vector2> sourcePositions)
    {
        foreach (var sourcePosition in sourcePositions)
        {
            if (DistanceSquaredToSegment(sourcePosition, start, end) <= _radarRenderDistanceSq)
            {
                return true;
            }
        }

        return false;
    }

    private static float DistanceSquaredToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();

        if (lengthSquared <= float.Epsilon)
            return Vector2.DistanceSquared(point, start);

        var t = Vector2.Dot(point - start, segment) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        var closestPoint = start + segment * t;
        return Vector2.DistanceSquared(point, closestPoint);
    }
}

public record struct BlipData
(
    NetEntity NetUid,
    EntityCoordinates Position,
    Angle Rotation,
    EntityUid? GridUid,
    BlipConfig Config,
    float Alpha
);
