using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._VRS.Planet;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server._VRS.Planet;

/// <summary>
/// Backend service for the Landgrab system. Handles purchasing plot grids at
/// arbitrary world locations on a planet, serializing/deserializing player
/// outposts to per-player YAML files, and overlap checks.
/// </summary>
public sealed class LandgrabPlotService : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapSys = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly SharedPvsOverrideSystem _pvs = default!;

    private ISawmill _sawmill = default!;

    /// <summary>Base directory under UserData where plot YAML files are stored.</summary>
    public const string SaveRoot = "/planet_plots";

    /// <summary>Allowed slot-name characters: letters, digits, dash, underscore.</summary>
    private static readonly Regex ValidSlotName = new(@"^[A-Za-z0-9_\-]{1,32}$");

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("landgrab");

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<LandgrabPlotComponent, ComponentInit>(OnPlotInit);
    }

    /// <summary>
    /// Whenever a plot becomes active (purchase, load from save, restored from
    /// a serialised map), make sure clients receive it regardless of distance
    /// so the border overlay can render it.
    /// </summary>
    private void OnPlotInit(Entity<LandgrabPlotComponent> ent, ref ComponentInit args)
    {
        _pvs.AddGlobalOverride(ent.Owner);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Auto-save every active player plot under a "_autosave" slot so progress
        // isn't lost if a player forgets to manually save before round end.
        var q = EntityQueryEnumerator<LandgrabPlotComponent, MapGridComponent>();
        while (q.MoveNext(out var gridUid, out var plot, out _))
        {
            if (string.IsNullOrEmpty(plot.OwnerCKey))
                continue;
            TrySavePlot(plot.OwnerCKey, gridUid, "_autosave", out _);
        }
    }

    // ── Registry / planet helpers ─────────────────────────────────────────────

    public bool TryGetRegistry(MapId mapId, out PlanetPlotRegistryComponent? registry)
    {
        registry = null;
        var q = EntityQueryEnumerator<PlanetPlotRegistryComponent, TransformComponent>();
        while (q.MoveNext(out _, out var comp, out var xform))
        {
            if (xform.MapID == mapId)
            {
                registry = comp;
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns true if a plot owned by the given player already exists.</summary>
    public bool TryGetPlayerPlot(string ckey, out EntityUid gridUid, out LandgrabPlotComponent? plot)
    {
        var q = EntityQueryEnumerator<LandgrabPlotComponent>();
        while (q.MoveNext(out var uid, out var comp))
        {
            if (comp.OwnerCKey == ckey)
            {
                gridUid = uid;
                plot = comp;
                return true;
            }
        }
        gridUid = EntityUid.Invalid;
        plot = null;
        return false;
    }

    /// <summary>
    /// Checks whether a plot of <paramref name="plotSize"/> centered at the given
    /// world position overlaps any existing plot. Excludes <paramref name="ignoreGrid"/> (e.g. when re-checking own grid).
    /// </summary>
    public bool IsLocationFree(MapId mapId, Vector2 worldPos, int plotSize, int spacing,
        out string? blockedReason, EntityUid? ignoreGrid = null)
    {
        blockedReason = null;
        var halfNew = (plotSize / 2f) + spacing;
        var newAabb = new Box2(worldPos - new Vector2(halfNew, halfNew), worldPos + new Vector2(halfNew, halfNew));

        var q = EntityQueryEnumerator<LandgrabPlotComponent, MapGridComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var plot, out var grid, out var xform))
        {
            if (xform.MapID != mapId)
                continue;
            if (ignoreGrid is { } ig && uid == ig)
                continue;

            var center = _transform.GetWorldPosition(uid);
            var halfOther = (plot.PlotSize / 2f) + spacing;
            var otherAabb = new Box2(center - new Vector2(halfOther, halfOther), center + new Vector2(halfOther, halfOther));

            if (newAabb.Intersects(otherAabb))
            {
                blockedReason = "landgrab-blocked-overlap";
                return false;
            }
        }
        return true;
    }

    // ── Purchase ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an empty plot grid centered at <paramref name="worldPos"/> on the
    /// given planet map and registers it to the player. Charges the registry's PurchaseCost.
    /// </summary>
    public bool TryPurchasePlot(string ckey, string ownerName, EntityUid playerEntity,
        MapId mapId, Vector2 worldPos, out string? failReason)
    {
        failReason = null;

        if (!TryGetRegistry(mapId, out var registry))
        {
            failReason = "landgrab-not-on-planet";
            return false;
        }

        if (TryGetPlayerPlot(ckey, out _, out _))
        {
            failReason = "landgrab-already-owns";
            return false;
        }

        if (!IsLocationFree(mapId, worldPos, registry!.PlotSize, registry.MinPlotSpacing, out var reason))
        {
            failReason = reason;
            return false;
        }

        if (!_bank.TryBankWithdraw(playerEntity, registry.PurchaseCost))
        {
            failReason = "landgrab-insufficient-funds";
            return false;
        }

        var grid = _mapManager.CreateGridEntity(mapId);
        _transform.SetWorldPosition(grid.Owner, worldPos);
        _metaData.SetEntityName(grid.Owner, $"{ownerName}'s outpost");

        var plot = AddComp<LandgrabPlotComponent>(grid.Owner);
        plot.OwnerCKey = ckey;
        plot.OwnerName = ownerName;
        plot.PlotSize = registry.PlotSize;
        Dirty(grid.Owner, plot);

        // Send the plot grid (and therefore its LandgrabPlotComponent) to all
        // clients regardless of distance, so the border overlay can render it
        // even when the camera is far away.
        _pvs.AddGlobalOverride(grid.Owner);

        _sawmill.Info($"Purchased plot for {ckey} at ({worldPos.X:F0}, {worldPos.Y:F0}).");
        return true;
    }

    /// <summary>
    /// Abandons the player's current plot. The grid is left in place but ownership
    /// is cleared; admin cleanup or another player can claim it.
    /// </summary>
    public bool TryAbandonPlot(string ckey, out string? failReason)
    {
        failReason = null;
        if (!TryGetPlayerPlot(ckey, out var gridUid, out var plot))
        {
            failReason = "landgrab-no-plot";
            return false;
        }

        // Just delete the grid entirely — the player wanted out, and abandoned grids
        // would otherwise litter the planet. Anything inside is forfeit.
        EntityManager.DeleteEntity(gridUid);
        _sawmill.Info($"Abandoned plot owned by {ckey}.");
        return true;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes the player's current plot grid to YAML and writes it to
    /// UserData/planet_plots/{ckey}/{slotName}.yml plus a sibling .meta file.
    /// </summary>
    public bool TrySavePlot(string ckey, EntityUid gridUid, string slotName, out string? failReason)
    {
        failReason = null;

        if (!ValidSlotName.IsMatch(slotName))
        {
            failReason = "landgrab-invalid-slot";
            return false;
        }

        if (!TryComp<LandgrabPlotComponent>(gridUid, out var plot) || plot.OwnerCKey != ckey)
        {
            failReason = "landgrab-no-plot";
            return false;
        }

        if (!TrySerializeGrid(gridUid, slotName, out var yaml))
        {
            failReason = "landgrab-serialize-failed";
            return false;
        }

        try
        {
            var ud = _resourceManager.UserData;
            var dirPath = new ResPath($"{SaveRoot}/{ckey}");
            ud.CreateDir(dirPath);

            var filePath = new ResPath($"{SaveRoot}/{ckey}/{slotName}.yml");
            using (var stream = ud.OpenWrite(filePath))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(yaml);
            }

            // Write metadata sidecar.
            if (TryComp<MapGridComponent>(gridUid, out var grid))
            {
                var aabb = grid.LocalAABB;
                var tileCount = _mapSys.GetAllTiles(gridUid, grid).Count();
                var metaPath = new ResPath($"{SaveRoot}/{ckey}/{slotName}.meta");
                using var stream = ud.OpenWrite(metaPath);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"width={(int)MathF.Ceiling(aabb.Width)}");
                writer.WriteLine($"height={(int)MathF.Ceiling(aabb.Height)}");
                writer.WriteLine($"tiles={tileCount}");
            }

            _sawmill.Info($"Saved plot for {ckey} as slot '{slotName}'.");
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to write plot save for {ckey}/{slotName}: {ex}");
            failReason = "landgrab-save-failed";
            return false;
        }
    }

    /// <summary>
    /// Lists the player's saved slots with metadata for the cartridge UI.
    /// </summary>
    public List<SavedPlotInfo> ListSavedPlots(string ckey, int loadCostBase, int loadCostPerTile)
    {
        var list = new List<SavedPlotInfo>();
        try
        {
            var ud = _resourceManager.UserData;
            var dirPath = new ResPath($"{SaveRoot}/{ckey}");
            if (!ud.Exists(dirPath))
                return list;

            foreach (var entry in ud.DirectoryEntries(dirPath))
            {
                if (!entry.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var slotName = entry[..^4];
                var info = new SavedPlotInfo { SlotName = slotName };
                ReadMeta(ud, ckey, slotName, info);
                info.LoadCost = loadCostBase + info.TileCount * loadCostPerTile;
                list.Add(info);
            }
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Failed to list saves for {ckey}: {ex}");
        }

        list.Sort((a, b) => string.Compare(a.SlotName, b.SlotName, StringComparison.Ordinal));
        return list;
    }

    private static void ReadMeta(IWritableDirProvider ud, string ckey, string slotName, SavedPlotInfo info)
    {
        try
        {
            var metaPath = new ResPath($"{SaveRoot}/{ckey}/{slotName}.meta");
            if (!ud.Exists(metaPath))
                return;

            using var stream = ud.OpenRead(metaPath);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var idx = line.IndexOf('=');
                if (idx < 0) continue;
                var key = line[..idx];
                var val = line[(idx + 1)..];
                if (!int.TryParse(val, out var n)) continue;
                switch (key)
                {
                    case "width": info.Width = n; break;
                    case "height": info.Height = n; break;
                    case "tiles": info.TileCount = n; break;
                }
            }
        }
        catch
        {
            // Missing/corrupt meta is non-fatal; UI just shows zeros.
        }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the named slot's YAML and places the grid centered at <paramref name="worldPos"/> on the planet.
    /// Charges <paramref name="loadCost"/> from the player. The player must NOT already own a plot.
    /// </summary>
    public bool TryLoadPlot(string ckey, string ownerName, EntityUid playerEntity,
        MapId mapId, Vector2 worldPos, string slotName, int loadCost, out string? failReason)
    {
        failReason = null;

        if (!ValidSlotName.IsMatch(slotName))
        {
            failReason = "landgrab-invalid-slot";
            return false;
        }

        if (!TryGetRegistry(mapId, out var registry))
        {
            failReason = "landgrab-not-on-planet";
            return false;
        }

        if (TryGetPlayerPlot(ckey, out _, out _))
        {
            failReason = "landgrab-already-owns";
            return false;
        }

        if (!IsLocationFree(mapId, worldPos, registry!.PlotSize, registry.MinPlotSpacing, out var reason))
        {
            failReason = reason;
            return false;
        }

        if (!TryReadSaveFile(ckey, slotName, out var yaml))
        {
            failReason = "landgrab-no-save";
            return false;
        }

        if (!_bank.TryBankWithdraw(playerEntity, loadCost))
        {
            failReason = "landgrab-insufficient-funds";
            return false;
        }

        if (!TryLoadGridFromYaml(yaml, slotName, mapId, worldPos, out var newGrid))
        {
            failReason = "landgrab-load-failed";
            return false;
        }

        var plot = EnsureComp<LandgrabPlotComponent>(newGrid);
        plot.OwnerCKey = ckey;
        plot.OwnerName = ownerName;
        plot.PlotSize = registry.PlotSize;
        Dirty(newGrid, plot);

        // Mirror the purchase path: keep the loaded plot grid globally visible
        // so the border overlay always renders it.
        _pvs.AddGlobalOverride(newGrid);

        _metaData.SetEntityName(newGrid, $"{ownerName}'s outpost");
        _sawmill.Info($"Loaded saved plot '{slotName}' for {ckey} at ({worldPos.X:F0}, {worldPos.Y:F0}).");
        return true;
    }

    /// <summary>Deletes a saved slot from disk.</summary>
    public bool TryDeleteSave(string ckey, string slotName, out string? failReason)
    {
        failReason = null;
        if (!ValidSlotName.IsMatch(slotName))
        {
            failReason = "landgrab-invalid-slot";
            return false;
        }
        try
        {
            var ud = _resourceManager.UserData;
            var ymlPath = new ResPath($"{SaveRoot}/{ckey}/{slotName}.yml");
            var metaPath = new ResPath($"{SaveRoot}/{ckey}/{slotName}.meta");
            if (ud.Exists(ymlPath))
                ud.Delete(ymlPath);
            if (ud.Exists(metaPath))
                ud.Delete(metaPath);
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Failed to delete save {ckey}/{slotName}: {ex}");
            failReason = "landgrab-save-failed";
            return false;
        }
    }

    private bool TryReadSaveFile(string ckey, string slotName, out string yaml)
    {
        yaml = string.Empty;
        try
        {
            var ud = _resourceManager.UserData;
            var path = new ResPath($"{SaveRoot}/{ckey}/{slotName}.yml");
            if (!ud.Exists(path))
                return false;
            using var stream = ud.OpenRead(path);
            using var reader = new StreamReader(stream);
            yaml = reader.ReadToEnd();
            return !string.IsNullOrWhiteSpace(yaml);
        }
        catch
        {
            return false;
        }
    }

    // ── Grid (de)serialization ────────────────────────────────────────────────

    private bool TrySerializeGrid(EntityUid gridUid, string slotName, out string yaml)
    {
        yaml = string.Empty;
        if (!HasComp<MapGridComponent>(gridUid))
            return false;

        try
        {
            var entities = new HashSet<EntityUid> { gridUid };
            var opts = SerializationOptions.Default with
            {
                MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
                ErrorOnOrphan = false,
                LogAutoInclude = null,
            };
            var (node, _) = _mapLoader.SerializeEntitiesRecursive(entities, opts);
            yaml = WriteNodeToString(node);
            return !string.IsNullOrWhiteSpace(yaml);
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Serialize failed for slot '{slotName}': {ex}");
            return false;
        }
    }

    private bool TryLoadGridFromYaml(string yaml, string slotName, MapId mapId, Vector2 worldPos, out EntityUid gridUid)
    {
        gridUid = EntityUid.Invalid;
        try
        {
            var options = new MapLoadOptions
            {
                MergeMap = mapId,
                Offset = worldPos,
                DeserializationOptions = DeserializationOptions.Default,
                ExpectedCategory = FileCategory.Grid,
            };
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
            if (!_mapLoader.TryLoadGeneric(stream, $"{slotName}.yml", out var result, options) || result == null)
                return false;

            if (result.Grids.Count != 1)
            {
                _mapLoader.Delete(result);
                return false;
            }

            gridUid = result.Grids.First().Owner;
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Load failed for slot '{slotName}': {ex}");
            return false;
        }
    }

    private static string WriteNodeToString(MappingDataNode node)
    {
        var document = new YamlDocument(node.ToYaml());
        using var writer = new StringWriter();
        var stream = new YamlStream { document };
        stream.Save(new YamlMappingFix(new Emitter(writer)), false);
        return writer.ToString();
    }
}
