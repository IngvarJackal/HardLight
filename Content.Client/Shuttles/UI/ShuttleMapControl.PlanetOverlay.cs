using System.Numerics;
using Content.Shared._VRS.Planet;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// VRS extension to <see cref="ShuttleMapControl"/>: when the currently-viewed
/// destination map is a biome planet, draw a coloured tile preview so pilots
/// can see solid rock vs. open ground before committing to a free-coord FTL.
/// Also overlays markers for any inserted coordinate disk that has been
/// engraved by the Landgrab cartridge with a specific landing offset.
/// </summary>
public sealed partial class ShuttleMapControl
{
    /// <summary>
    /// Maximum quads sampled per frame. Above this the stride grows so the
    /// overlay degrades gracefully when zoomed all the way out. Set to a
    /// modest 48×48 — at typical console resolutions individual quads below
    /// this size are sub-pixel and indistinguishable.
    /// </summary>
    private const int MaxOverlayQuads = 48 * 48;

    /// <summary>
    /// Minimum sampling stride. A stride of 1 samples every world tile, which
    /// is wasteful at any practical console zoom (each tile is &lt;= a couple
    /// pixels). Floor at 2 to halve the per-frame quad count for free.
    /// </summary>
    private const int MinOverlayStride = 2;

    // Frame cache: as long as the viewport, map, and stride don't change, we
    // can re-use the previously-sampled grid instead of re-running thousands
    // of TryGetTile calls every render. Idle/static pilots get O(1) cost.
    private struct OverlaySample
    {
        public Vector2 WorldCenter;
        public Color Color;
    }

    private EntityUid _overlayCacheMap;
    private int _overlayCacheMinX;
    private int _overlayCacheMinY;
    private int _overlayCacheMaxX;
    private int _overlayCacheMaxY;
    private int _overlayCacheStride;
    private int _overlayCacheSeed;
    private List<OverlaySample>? _overlayCache;

    /// <summary>
    /// Draws the biome tile preview for <paramref name="viewedMapUid"/> if it
    /// has a <see cref="BiomeComponent"/>. Safe to call on non-biome maps —
    /// will return immediately. Coloring is keyed off tile def IDs (no
    /// engine changes needed): snow/ice → pale, grass/plant → green,
    /// sand/desert → tan, lava → red, water → blue, asteroid/rock/floor →
    /// grey-brown; everything else → mid brown.
    /// </summary>
    private void DrawPlanetBiomeOverlay(DrawingHandleScreen handle, EntityUid viewedMapUid,
        Box2 viewBox, Matrix3x2 matty)
    {
        if (!EntManager.TryGetComponent(viewedMapUid, out BiomeComponent? biome))
            return;

        // Lazy-resolve once. SharedBiomeSystem.TryGetTile is the public path;
        // it handles BiomeMetaLayer recursion and respects per-layer noise/threshold.
        if (_biomeSystem == null)
            _biomeSystem = EntManager.System<SharedBiomeSystem>();
        if (_tileDefs == null)
            _tileDefs = IoCManager.Resolve<ITileDefinitionManager>();

        // Choose stride so we never exceed the per-frame quad budget. A stride
        // of MinOverlayStride = sample every Nth tile; grows from there.
        var widthTiles = (int)MathF.Ceiling(viewBox.Width);
        var heightTiles = (int)MathF.Ceiling(viewBox.Height);
        var rawCount = widthTiles * heightTiles;
        var stride = MinOverlayStride;
        while (rawCount / (stride * stride) > MaxOverlayQuads)
            stride++;

        var quadSizePx = MinimapScale * stride;
        // Inflate slightly so adjacent quads visually touch without gaps after rounding.
        var quadHalfPad = 0.5f;
        var quadDrawSize = new Vector2(quadSizePx + quadHalfPad, quadSizePx + quadHalfPad);

        var minX = (int)MathF.Floor(viewBox.Left);
        var minY = (int)MathF.Floor(viewBox.Bottom);
        var maxX = (int)MathF.Ceiling(viewBox.Right);
        var maxY = (int)MathF.Ceiling(viewBox.Top);

        // Snap to stride grid so coloring is stable when panning.
        minX -= ((minX % stride) + stride) % stride;
        minY -= ((minY % stride) + stride) % stride;

        // Cache key: any change in the visible region, stride, or biome seed
        // (e.g. admin re-rolls the planet) invalidates the sampled grid.
        var samples = _overlayCache;
        if (samples == null
            || _overlayCacheMap != viewedMapUid
            || _overlayCacheMinX != minX
            || _overlayCacheMinY != minY
            || _overlayCacheMaxX != maxX
            || _overlayCacheMaxY != maxY
            || _overlayCacheStride != stride
            || _overlayCacheSeed != biome.Seed)
        {
            samples = new List<OverlaySample>(Math.Min(MaxOverlayQuads, ((maxX - minX) / stride + 1) * ((maxY - minY) / stride + 1)));
            for (var y = minY; y < maxY; y += stride)
            {
                for (var x = minX; x < maxX; x += stride)
                {
                    if (!_biomeSystem.TryGetTile(new Vector2i(x, y), biome.Layers, biome.Seed, null, out var tile))
                        continue;

                    var def = _tileDefs[tile.Value.TypeId];
                    samples.Add(new OverlaySample
                    {
                        WorldCenter = new Vector2(x + 0.5f * stride, y + 0.5f * stride),
                        Color = ColorForTile(def),
                    });
                }
            }

            _overlayCache = samples;
            _overlayCacheMap = viewedMapUid;
            _overlayCacheMinX = minX;
            _overlayCacheMinY = minY;
            _overlayCacheMaxX = maxX;
            _overlayCacheMaxY = maxY;
            _overlayCacheStride = stride;
            _overlayCacheSeed = biome.Seed;
        }

        // Draw pass: just transforms + DrawRect; no FastNoise sampling on cache hits.
        foreach (var s in samples)
        {
            var local = Vector2.Transform(s.WorldCenter, matty);
            local = local with { Y = -local.Y };
            var screen = ScalePosition(local);

            handle.DrawRect(
                new UIBox2(screen - quadDrawSize / 2f, screen + quadDrawSize / 2f),
                s.Color);
        }
    }

    private static Color ColorForTile(ITileDefinition def)
    {
        var id = def.ID;
        // Cheap substring routing — tile IDs in this codebase follow conventions
        // like "FloorAsteroid", "FloorSnow", "FloorGrass", "Plating", "Lava", etc.
        if (Contains(id, "Snow") || Contains(id, "Ice")) return new Color(0.85f, 0.90f, 0.95f, 0.70f);
        if (Contains(id, "Lava")) return new Color(0.85f, 0.25f, 0.10f, 0.70f);
        if (Contains(id, "Water") || Contains(id, "Ocean")) return new Color(0.20f, 0.40f, 0.70f, 0.70f);
        if (Contains(id, "Grass") || Contains(id, "Plant") || Contains(id, "Jungle")) return new Color(0.30f, 0.55f, 0.25f, 0.70f);
        if (Contains(id, "Sand") || Contains(id, "Desert")) return new Color(0.80f, 0.70f, 0.45f, 0.70f);
        if (Contains(id, "Asteroid") || Contains(id, "Rock") || Contains(id, "Stone") || Contains(id, "Cave"))
            return new Color(0.35f, 0.32f, 0.30f, 0.80f); // darker = "solid", visually reads as wall
        if (Contains(id, "Plating") || Contains(id, "Steel")) return new Color(0.50f, 0.50f, 0.55f, 0.70f);
        return new Color(0.45f, 0.38f, 0.30f, 0.60f); // generic mid brown
    }

    private static bool Contains(string source, string fragment) =>
        source.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Draws labelled markers for any held/inserted coordinate disk that has been
    /// engraved by the Landgrab cartridge with a landing offset on the currently
    /// viewed map. Pilots can see exactly where their saved spots are before
    /// clicking to FTL there.
    /// </summary>
    private void DrawLandgrabDiskMarkers(DrawingHandleScreen handle, EntityUid viewedMapUid, Matrix3x2 matty)
    {
        var viewedNet = EntManager.GetNetEntity(viewedMapUid);
        var enumerator = EntManager.AllEntityQueryEnumerator<LandgrabCoordinatesComponent>();
        while (enumerator.MoveNext(out _, out var disk))
        {
            if (disk.Destination != viewedNet)
                continue;

            var local = Vector2.Transform(disk.Offset, matty);
            local = local with { Y = -local.Y };
            var screen = ScalePosition(local);

            // Crosshair: outer ring + small filled center.
            handle.DrawCircle(screen, 8f, new Color(1f, 0.85f, 0.20f), filled: false);
            handle.DrawCircle(screen, 2.5f, new Color(1f, 0.85f, 0.20f));

            if (!string.IsNullOrEmpty(disk.Label))
            {
                handle.DrawString(_font, screen + new Vector2(10f, -6f),
                    disk.Label, new Color(1f, 0.85f, 0.20f));
            }
        }
    }

    /// <summary>
    /// Draws a cyan box approximating each player plot's footprint plus the
    /// owner's name. The local player's own plot is drawn in white so it
    /// stands out from other players' plots. Plots are real grids that already
    /// render via the standard map-grid pass, but the explicit marker makes
    /// ownership and footprint instantly readable when a pilot is choosing
    /// where to land.
    /// </summary>
    private void DrawLandgrabPlotMarkers(DrawingHandleScreen handle, EntityUid viewedMapUid, Matrix3x2 matty)
    {
        var otherColor = new Color(0.30f, 0.85f, 0.95f, 0.85f);
        var ownColor   = new Color(1.0f,  1.0f,  1.0f,  1.0f);

        var localCKey = _playerManager.LocalSession?.Name ?? string.Empty;

        var enumerator = EntManager.AllEntityQueryEnumerator<LandgrabPlotComponent, TransformComponent>();
        while (enumerator.MoveNext(out var plotUid, out var plot, out var xform))
        {
            if (xform.MapUid != viewedMapUid)
                continue;

            var isOwn = !string.IsNullOrEmpty(localCKey)
                && string.Equals(plot.OwnerCKey, localCKey, StringComparison.OrdinalIgnoreCase);

            var color = isOwn ? ownColor : otherColor;

            var worldPos = _xformSystem.GetWorldPosition(plotUid);
            var local = Vector2.Transform(worldPos, matty);
            local = local with { Y = -local.Y };
            var screen = ScalePosition(local);

            // Plot footprint outline at current zoom (PlotSize is in world tiles).
            var halfSize = (plot.PlotSize / 2f) * MinimapScale;
            handle.DrawRect(
                new UIBox2(screen - new Vector2(halfSize, halfSize), screen + new Vector2(halfSize, halfSize)),
                color, filled: false);

            handle.DrawCircle(screen, 2.5f, color);

            if (!string.IsNullOrEmpty(plot.OwnerName))
            {
                handle.DrawString(_font, screen + new Vector2(8f, -12f), plot.OwnerName, color);
            }
        }
    }

    /// <summary>
    /// Draws red diamond markers (with config-name label) for every dungeon the
    /// planet's <see cref="PlanetDungeonRegistryComponent"/> reports as spawned.
    /// Server-authoritative, so dungeons in unloaded biome chunks still appear.
    /// </summary>
    private void DrawDungeonMarkers(DrawingHandleScreen handle, EntityUid viewedMapUid, Matrix3x2 matty)
    {
        if (!EntManager.TryGetComponent(viewedMapUid, out PlanetDungeonRegistryComponent? registry))
            return;

        var color = new Color(0.95f, 0.30f, 0.30f, 0.95f);
        const float s = 6f;

        foreach (var marker in registry.Dungeons)
        {
            var local = Vector2.Transform(marker.Position, matty);
            local = local with { Y = -local.Y };
            var screen = ScalePosition(local);

            // Diamond outline.
            handle.DrawLine(screen + new Vector2(0, -s), screen + new Vector2(s, 0), color);
            handle.DrawLine(screen + new Vector2(s, 0), screen + new Vector2(0, s), color);
            handle.DrawLine(screen + new Vector2(0, s), screen + new Vector2(-s, 0), color);
            handle.DrawLine(screen + new Vector2(-s, 0), screen + new Vector2(0, -s), color);

            if (!string.IsNullOrEmpty(marker.Name))
            {
                handle.DrawString(_font, screen + new Vector2(8f, -6f), marker.Name, color);
            }
        }
    }

    // Cached system/manager handles populated lazily on first overlay draw.
    private SharedBiomeSystem? _biomeSystem;
    private ITileDefinitionManager? _tileDefs;
}
