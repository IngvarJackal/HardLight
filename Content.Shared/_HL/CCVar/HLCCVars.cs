using System.ComponentModel;
using System.Runtime.InteropServices.Marshalling;
using Robust.Shared.Configuration;

namespace Content.Shared.HL.CCVar;

/// <summary>
/// Configuration variables for HardLight-specific features
/// </summary>
[CVarDefs]
public sealed class HLCCVars
{
    /// <summary>
    /// Enable round persistence system to maintain ship functionality across round restarts
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceEnabled =
        CVarDef.Create("hardlight.round_persistence.enabled", false, CVar.SERVERONLY);

    /// <summary>
    /// Enable expedition data persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceExpeditions =
        CVarDef.Create("hardlight.round_persistence.expeditions", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable shuttle records persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceShuttleRecords =
        CVarDef.Create("hardlight.round_persistence.shuttle_records", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable station records and manifest persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceStationRecords =
        CVarDef.Create("hardlight.round_persistence.station_records", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable ship IFF and association persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceShipData =
        CVarDef.Create("hardlight.round_persistence.ship_data", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable player payment data persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistencePlayerPayments =
        CVarDef.Create("hardlight.round_persistence.player_payments", true, CVar.SERVERONLY);

    /// <summary>
    /// Maximum number of rounds to keep persistence data for
    /// </summary>
    public static readonly CVarDef<int> RoundPersistenceMaxRounds =
        CVarDef.Create("hardlight.round_persistence.max_rounds", 500, CVar.SERVERONLY);

    /// <summary>
    /// Enable verbose logging for the persistence system
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceDebugLogging =
        CVarDef.Create("hardlight.round_persistence.debug_logging", false, CVar.SERVERONLY);

    // Vending performance controls
    public static readonly CVarDef<bool> VendingLazyRestock =
        CVarDef.Create("hardlight.vending.lazy_restock", true, CVar.SERVERONLY);

    public static readonly CVarDef<int> VendingRestockBatch =
        CVarDef.Create("hardlight.vending.restock_batch", 2, CVar.SERVERONLY, desc: "Max vending machines to restock per tick.");

    public static readonly CVarDef<int> VendingRestockTickMs =
        CVarDef.Create("hardlight.vending.restock_tick_ms", 250, CVar.SERVERONLY, desc: "Interval in ms between vending restock ticks.");

    // Exclude vending machines from shuttle save files to avoid load-time spikes
    public static readonly CVarDef<bool> ExcludeVendingInShipSave =
        CVarDef.Create("hardlight.vending.exclude_in_ship_save", true, CVar.SERVERONLY,
            desc: "If true, vending machines are omitted from shuttle save files to reduce load hitches.");

    // World chunk logging / behavior
    public static readonly CVarDef<bool> WorldChunkDebugLogs =
        CVarDef.Create("hardlight.world.debug_chunk_logs", false, CVar.SERVERONLY, desc: "Enable world chunk load/unload debug logs.");

    // Ship load performance controls
    public static readonly CVarDef<bool> ShipLoadAsync =
        CVarDef.Create("hardlight.shipload.async", true, CVar.SERVERONLY, desc: "Load ships asynchronously over multiple ticks.");

    public static readonly CVarDef<int> ShipLoadBatchNonContained =
        CVarDef.Create("hardlight.shipload.batch_noncontained", 128, CVar.SERVERONLY, desc: "Max non-contained entities to spawn per tick during ship load.");

    public static readonly CVarDef<int> ShipLoadBatchContained =
        CVarDef.Create("hardlight.shipload.batch_contained", 128, CVar.SERVERONLY, desc: "Max contained entities to spawn/insert per tick during ship load.");

    public static readonly CVarDef<int> ShipLoadTimeBudgetMs =
        CVarDef.Create("hardlight.shipload.time_budget_ms", 8, CVar.SERVERONLY, desc: "Soft time budget per tick for ship load processing (ms).");

    public static readonly CVarDef<bool> ShipLoadDecals =
        CVarDef.Create("hardlight.shipload.load_decals", false, CVar.SERVERONLY, desc: "If true, restore decals when loading ships (can be expensive).");

    public static readonly CVarDef<bool> ShipLoadLogProgress =
        CVarDef.Create("hardlight.shipload.log_progress", false, CVar.SERVERONLY, desc: "Log ship load progress each tick.");

    // Shipyard purchase docking-search caps. The full N×M dock-pair search becomes pathological
    // on large stations + large ships (140 × 40 = 5,600 candidate pairs, each running CanDock,
    // FindGridsIntersecting, and an inner aggregation sweep). On purchase we only need *a* valid
    // dock — not the global optimum — so we sample a spatially-spread, priority-aware subset of
    // docks from each side. If the capped search returns nothing, the call site falls back to the
    // full uncapped search so a purchase can never fail solely due to the optimization.
    public static readonly CVarDef<bool> ShipyardPurchaseDockCapEnabled =
        CVarDef.Create("hardlight.shipyard.purchase_dock_cap_enabled", true, CVar.SERVERONLY,
            desc: "If true, shipyard purchase docking only considers a capped, spatially spread subset of docks per side. Falls back to full search on miss.");

    public static readonly CVarDef<int> ShipyardPurchaseDockCapShuttle =
        CVarDef.Create("hardlight.shipyard.purchase_dock_cap_shuttle", 8, CVar.SERVERONLY,
            desc: "Max shuttle-side docks considered during shipyard purchase docking. <= 0 disables the cap on this side.");

    public static readonly CVarDef<int> ShipyardPurchaseDockCapGrid =
        CVarDef.Create("hardlight.shipyard.purchase_dock_cap_grid", 12, CVar.SERVERONLY,
            desc: "Max station-side docks considered during shipyard purchase docking. <= 0 disables the cap on this side.");

    // HardLight: Emergency shuttle/escape-pod docking-search caps. Evac hits the same pathological
    // dock-pair search as shipyard purchase, but on a much more player-visible path: once when
    // the shuttle is called to station and again shortly before launch when it precomputes its
    // ColComm destination. We preserve priority docks in the capped pass and still fall back to
    // the full search on miss so this optimization cannot strand the shuttle.
    public static readonly CVarDef<bool> EmergencyShuttleDockCapEnabled =
        CVarDef.Create("hardlight.emergency_shuttle.dock_cap_enabled", true, CVar.SERVERONLY,
            desc: "If true, emergency shuttle and escape pod docking use a capped, spatially spread subset of docks per side before falling back to the full search.");

    public static readonly CVarDef<int> EmergencyShuttleDockCapShuttle =
        CVarDef.Create("hardlight.emergency_shuttle.dock_cap_shuttle", 8, CVar.SERVERONLY,
            desc: "Max shuttle-side docks considered during emergency shuttle and escape pod docking. <= 0 disables the cap on this side.");

    public static readonly CVarDef<int> EmergencyShuttleDockCapGrid =
        CVarDef.Create("hardlight.emergency_shuttle.dock_cap_grid", 12, CVar.SERVERONLY,
            desc: "Max destination-side docks considered during emergency shuttle and escape pod docking. <= 0 disables the cap on this side.");

    /// <summary>
    /// HardLight: number of UseDelay component resets to process per tick during the post-load
    /// sanitize phase of a freshly loaded ship. Set to 0 to disable spreading and do all resets
    /// synchronously (original behavior).
    /// </summary>
    public static readonly CVarDef<int> ShipLoadDeferredUseDelayBudget =
        CVarDef.Create("hardlight.shipload.deferred_usedelay_budget", 32, CVar.SERVERONLY,
            desc: "Per-tick budget for deferred UseDelay resets after ship load. 0 disables deferral (sync reset).");

    // Radar blip system performance controls.
    // Mono RadarBlipSystem.OnBlipsRequested can dominate server tick under combat / multi-console
    // load: each request runs EntityLookupSystem.GetEntitiesInRange (B2 broadphase + grid intersect)
    // around every radar source. The previous hardcoded 75ms cache TTL meant repeated requests from
    // the same console (or different consoles on the same map within range) re-ran the heavy gather
    // path roughly 13×/sec. These CVars expose both the per-user request floor and the per-console
    // cache TTL so server operators can trade radar refresh smoothness for tick budget headroom.
    public static readonly CVarDef<int> RadarMinRequestMs =
        CVarDef.Create("hardlight.radar.min_request_ms", 500, CVar.SERVERONLY,
            desc: "Minimum milliseconds between RadarBlipSystem requests accepted from the same player. Higher = less server work per radar console at the cost of update smoothness.");

    public static readonly CVarDef<int> RadarReportCacheTtlMs =
        CVarDef.Create("hardlight.radar.cache_ttl_ms", 400, CVar.SERVERONLY,
            desc: "Milliseconds a per-console assembled blip report is reused before re-running the entity lookup. Bounded by hardlight.radar.min_request_ms in practice; raise to amortize lookups across multiple viewers, lower for tighter freshness.");

    /// <summary>
    ///     Goobstation: Whether or not to allow mech weaponry to be used out of mechs.
    /// </summary>
    public static readonly CVarDef<bool> MechGunOutsideMech =
        CVarDef.Create("mech.gun_outside_mech", false, CVar.SERVERONLY, desc: "If true, allows mech weapons to be used outside of mechs.");

    /// <summary>
    /// Starlight: Sends afk players to cryo.
    /// </summary>
    public static readonly CVarDef<bool> CryoTeleportation =
        CVarDef.Create("game.cryo_teleportation", true, CVar.SERVERONLY);

    // Stumble-on-missing-leg tuning. Live-editable via the `cvar` console
    // command; values <= 0 fall back to the per-component DataField default.
    public static readonly CVarDef<float> StumbleChance =
        CVarDef.Create("hardlight.stumble.chance", 0.15f, CVar.SERVERONLY,
            desc: "Probability [0..1] a partial-leg entity stumbles when its distance threshold is reached. <=0 disables stumbles entirely.");

    public static readonly CVarDef<float> StumbleTiles =
        CVarDef.Create("hardlight.stumble.tiles", 10f, CVar.SERVERONLY,
            desc: "Tiles a partial-leg entity must walk between stumble checks. <=0 uses the per-component default.");

    public static readonly CVarDef<float> StumbleKnockdownSeconds =
        CVarDef.Create("hardlight.stumble.knockdown_seconds", 2.5f, CVar.SERVERONLY,
            desc: "How long the stumble knockdown lasts. <=0 uses the per-component default.");

    /// <summary>
    /// Starlight: Allows disabling the auto-spawning of colcomm...
    /// </summary>
    public static readonly CVarDef<bool> AutoSpawnColComm =
        CVarDef.Create("game.autospawn_colcomm", true, CVar.SERVERONLY);
}
