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
    ///     The maximum factor by which the event schedulers speed up when station heat is far below the round's
    ///     target heat (see <see cref="Content.Server.StationEvents.Components.HeatTargetRuleComponent"/>). 2 means
    ///     events fire up to twice as often on a too-quiet station. 1 disables the frequency boost.
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<float>
        EventsHeatMaxFrequencyMultiplier = CVarDef.Create("events.heat_max_frequency_mult", 2.0f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     The "average" event heat: events with no <c>EventHeat</c> component are treated as this cost, and it is the
    ///     margin added to the round's target heat to form the time-gated difficulty cap (<c>target + baseline</c>)
    ///     that decides which events are valid each hour. Heat only gates validity; base weights decide distribution.
    /// </summary>
    [CVarControl(AdminFlags.Server | AdminFlags.Mapping)]
    public static readonly CVarDef<float>
        EventsHeatBaseline = CVarDef.Create("events.heat_baseline", 50f, CVar.ARCHIVE | CVar.SERVERONLY);
}
