using System.Numerics;
using Content.Shared._VRS.Planet;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._VRS.Planet;

/// <summary>
/// Draws a ghosted border in the game world around every visible landgrab plot.
/// The local player's own plot is drawn in bright white; other players' plots
/// are drawn in a muted teal so ownership is immediately obvious.
/// Only visible when the camera is on a <see cref="PlanetPlotRegistryComponent"/> map.
/// </summary>
public sealed class LandgrabPlotBorderOverlay : Overlay
{
    // Draw on top of world entities/tiles so biome floors and walls don't
    // hide the ghosted plot border.
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    private readonly SharedTransformSystem _xform;

    /// <summary>Bright white used for the local player's own plot.</summary>
    private static readonly Color OwnPlotColor = new Color(1.0f, 1.0f, 1.0f, 0.85f);

    /// <summary>Soft fill drawn inside the local player's own plot.</summary>
    private static readonly Color OwnPlotFillColor = new Color(1.0f, 1.0f, 1.0f, 0.10f);

    /// <summary>Muted teal used for all other players' plots.</summary>
    private static readonly Color OtherPlotColor = new Color(0.40f, 0.95f, 1.00f, 0.65f);

    /// <summary>Border thickness in world units (≈ tiles).</summary>
    private const float BorderThickness = 0.25f;

    public LandgrabPlotBorderOverlay()
    {
        IoCManager.InjectDependencies(this);
        _xform = _entities.System<SharedTransformSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Only draw when on a planet map (has the registry component on the map entity).
        var mapUid = _mapManager.GetMapEntityId(args.MapId);
        return _entities.TryGetComponent(mapUid, out PlanetPlotRegistryComponent? _);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var localCKey = _player.LocalSession?.Name ?? string.Empty;

        var enumerator = _entities.AllEntityQueryEnumerator<LandgrabPlotComponent, TransformComponent>();
        while (enumerator.MoveNext(out _, out var plot, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var worldPos = _xform.GetWorldPosition(xform);
            var half = plot.PlotSize * 0.5f;

            var isOwn = string.Equals(plot.OwnerCKey, localCKey, StringComparison.OrdinalIgnoreCase);
            var color = isOwn ? OwnPlotColor : OtherPlotColor;

            var min = new Vector2(worldPos.X - half, worldPos.Y - half);
            var max = new Vector2(worldPos.X + half, worldPos.Y + half);

            // Soft tint inside our own plot makes it instantly recognisable.
            if (isOwn)
            {
                args.WorldHandle.DrawRect(new Box2(min, max), OwnPlotFillColor);
            }

            // Thick border drawn as four filled rects so the line is visible at
            // any zoom regardless of biome textures underneath.
            var t = BorderThickness;
            // Top
            args.WorldHandle.DrawRect(new Box2(min.X, max.Y - t, max.X, max.Y), color);
            // Bottom
            args.WorldHandle.DrawRect(new Box2(min.X, min.Y, max.X, min.Y + t), color);
            // Left
            args.WorldHandle.DrawRect(new Box2(min.X, min.Y, min.X + t, max.Y), color);
            // Right
            args.WorldHandle.DrawRect(new Box2(max.X - t, min.Y, max.X, max.Y), color);
        }
    }
}
