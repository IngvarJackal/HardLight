using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Controls if the game should run station events
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<bool>
        EventsEnabled = CVarDef.Create("events.enabled", true, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     The station "heat" (chaos/danger budget, measured roughly in minutes-of-chaos) ceiling. The event
    ///     schedulers will not pick an event whose <c>EventHeat.Cost</c> would push current heat past this value,
    ///     i.e. an event is only "affordable" while <c>currentHeat + Cost &lt;= ceiling</c>. Events with no heat
    ///     cost are never suppressed. Set very high to effectively disable heat-based suppression.
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<float>
        EventsHeatCeiling = CVarDef.Create("events.heat_ceiling", 300f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Heat dissipated per minute for each (rounded up) hour of round time elapsed.
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<float>
        EventsHeatDecayPerHour = CVarDef.Create("events.heat_decay_per_hour", 0.5f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Heat dissipated per minute for each active security crew member.
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<float>
        EventsHeatDecayPerSecurity = CVarDef.Create("events.heat_decay_per_security", 1.0f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Heat dissipated per minute for each active command crew member.
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<float>
        EventsHeatDecayPerCommand = CVarDef.Create("events.heat_decay_per_command", 0.5f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     How strongly low station heat biases event selection toward higher-cost (more dangerous) events.
    ///     An event's effective weight is multiplied by <c>1 + danger_bias * Cost</c>. 0 disables the bias
    ///     (pure base-weight selection, but affordability suppression still applies).
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<float>
        EventsDangerBias = CVarDef.Create("events.danger_bias", 0.004f, CVar.ARCHIVE | CVar.SERVERONLY);
}
