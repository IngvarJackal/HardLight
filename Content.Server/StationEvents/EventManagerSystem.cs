using System.Linq;
using Content.Server.GameTicking;
using Content.Server.RoundEnd;
using Content.Server.StationEvents.Components;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.EntityTable;
using Content.Server.Mind; // Frontier
using Content.Server._NF.Roles.Systems; // Frontier

using Content.Server.Psionics.Glimmer;
using Content.Shared.Psionics.Glimmer;
namespace Content.Server.StationEvents;

public sealed class EventManagerSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly EntityTableSystem _entityTable = default!;
    [Dependency] public readonly GameTicker GameTicker = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly JobTrackingSystem _jobs = default!; // Frontier
    [Dependency] private readonly GlimmerSystem _glimmerSystem = default!; //Nyano - Summary: pulls in the glimmer system.
    [Dependency] private readonly StationHeatSystem _stationHeat = default!; // HardLight - event heat/chaos budget


    public bool EventsEnabled { get; private set; }
    private void SetEnabled(bool value) => EventsEnabled = value;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, CCVars.EventsEnabled, SetEnabled, true);
    }

    /// <summary>
    /// Randomly runs a valid event.
    /// </summary>
    [Obsolete("use overload taking EnityTableSelector instead or risk unexpected results")]
    public void RunRandomEvent()
    {
        var randomEvent = PickRandomEvent();

        if (randomEvent == null)
        {
            var errStr = Loc.GetString("station-event-system-run-random-event-no-valid-events");
            Log.Error(errStr);
            return;
        }

        GameTicker.AddGameRule(randomEvent);
    }

    /// <summary>
    /// Randomly runs an event from provided EntityTableSelector.
    /// </summary>
    public void RunRandomEvent(EntityTableSelector limitedEventsTable)
    {
        var availableEvents = AvailableEvents(); // handles the player counts and individual event restrictions.
                                                 // Putting this here only makes any sense in the context of the toolshed commands in BasicStationEventScheduler. Kill me.

        if (!TryBuildLimitedEvents(limitedEventsTable, availableEvents, out var limitedEvents))
        {
            Log.Warning("Provided event table could not build dict!");
            return;
        }

        var randomLimitedEvent = FindEventWithHeat(limitedEvents); // HardLight: heat-aware pick (falls back to plain weighting when no heat data is present)
        if (randomLimitedEvent == null)
        {
            Log.Warning("The selected random event is null!");
            return;
        }

        if (!_prototype.TryIndex(randomLimitedEvent, out _))
        {
            Log.Warning("A requested event is not available!");
            return;
        }

        GameTicker.AddGameRule(randomLimitedEvent);
    }

    /// <summary>
    /// Returns true if the provided EntityTableSelector gives at least one prototype with a StationEvent comp.
    /// </summary>
    public bool TryBuildLimitedEvents(
        EntityTableSelector limitedEventsTable,
        Dictionary<EntityPrototype, StationEventComponent> availableEvents,
        out Dictionary<EntityPrototype, StationEventComponent> limitedEvents
        )
    {
        limitedEvents = new Dictionary<EntityPrototype, StationEventComponent>();

        if (availableEvents.Count == 0)
        {
            Log.Warning("No events were available to run!");
            return false;
        }

        var selectedEvents = _entityTable.GetSpawns(limitedEventsTable);

        if (selectedEvents.Any() != true) // This is here so if you fuck up the table it wont die.
            return false;

        foreach (var eventid in selectedEvents)
        {
            if (!_prototype.TryIndex(eventid, out var eventproto))
            {
                Log.Warning("An event ID has no prototype index!");
                continue;
            }

            if (limitedEvents.ContainsKey(eventproto)) // This stops it from dying if you add duplicate entries in a fucked table
                continue;

            if (eventproto.Abstract)
                continue;

            if (!eventproto.TryGetComponent<StationEventComponent>(out var stationEvent, EntityManager.ComponentFactory))
                continue;

            if (!availableEvents.ContainsKey(eventproto))
                continue;

            limitedEvents.Add(eventproto, stationEvent);
        }

        if (!limitedEvents.Any())
            return false;

        return true;
    }

    /// <summary>
    /// Randomly picks a valid event.
    /// </summary>
    public string? PickRandomEvent()
    {
        var availableEvents = AvailableEvents();
        Log.Info($"Picking from {availableEvents.Count} total available events");
        return FindEvent(availableEvents);
    }

    /// <summary>
    /// Pick a random event from the available events at this time, also considering their weightings.
    /// </summary>
    /// <returns></returns>
    public string? FindEvent(Dictionary<EntityPrototype, StationEventComponent> availableEvents)
    {
        if (availableEvents.Count == 0)
        {
            Log.Warning("No events were available to run!");
            return null;
        }

        var sumOfWeights = 0.0f;

        foreach (var stationEvent in availableEvents.Values)
        {
            sumOfWeights += stationEvent.Weight;
        }

        sumOfWeights = _random.NextFloat(sumOfWeights);

        foreach (var (proto, stationEvent) in availableEvents)
        {
            sumOfWeights -= stationEvent.Weight;

            if (sumOfWeights <= 0.0f)
            {
                return proto.ID;
            }
        }

        Log.Error("Event was not found after weighted pick process!");
        return null;
    }

    /// <summary>
    /// HardLight: Picks an event like <see cref="FindEvent"/>, but factors in the current station "heat"
    /// (see <see cref="StationHeatSystem"/> and <see cref="Components.EventHeatComponent"/>):
    /// <list type="bullet">
    ///     <item>Events whose heat cost would push past the remaining budget are suppressed, so the schedulers
    ///           stop piling new threats on top of an ongoing crisis (e.g. nukies / xenoborgs).</item>
    ///     <item>When the station is quiet, higher-cost (more dangerous) events are biased upward, so it escalates
    ///           instead of staying random.</item>
    /// </list>
    /// Fully back-compatible: events without an <see cref="Components.EventHeatComponent"/> have a cost of 0, are
    /// never suppressed, and keep their base weight, reproducing the original <see cref="FindEvent"/> behaviour.
    /// </summary>
    public string? FindEventWithHeat(Dictionary<EntityPrototype, StationEventComponent> availableEvents)
    {
        if (availableEvents.Count == 0)
        {
            Log.Warning("No events were available to run!");
            return null;
        }

        var ceiling = _configurationManager.GetCVar(CCVars.EventsHeatCeiling);
        var bias = _configurationManager.GetCVar(CCVars.EventsDangerBias);
        var headroom = ceiling - _stationHeat.CurrentHeat;

        var weighted = new List<(string Id, float Weight)>();
        var sumOfWeights = 0.0f;

        foreach (var (proto, stationEvent) in availableEvents)
        {
            var cost = GetEventCost(proto);

            // Suppress costly events when the station is already too chaotic. Free (cost 0) events are never suppressed.
            if (cost > 0.0f && cost > headroom)
                continue;

            var effectiveWeight = stationEvent.Weight * (1.0f + bias * cost);
            if (effectiveWeight <= 0.0f)
                continue;

            weighted.Add((proto.ID, effectiveWeight));
            sumOfWeights += effectiveWeight;
        }

        // Everything was suppressed (station saturated) or nothing had weight: run nothing this cycle.
        if (weighted.Count == 0 || sumOfWeights <= 0.0f)
            return null;

        sumOfWeights = _random.NextFloat(sumOfWeights);

        foreach (var (id, weight) in weighted)
        {
            sumOfWeights -= weight;

            if (sumOfWeights <= 0.0f)
                return id;
        }

        return weighted[^1].Id;
    }

    /// <summary>
    /// HardLight: Reads the optional heat cost off an event prototype. Returns 0 if it carries no
    /// <see cref="Components.EventHeatComponent"/>.
    /// </summary>
    private float GetEventCost(EntityPrototype proto)
    {
        if (proto.TryGetComponent<Components.EventHeatComponent>(out var heat, EntityManager.ComponentFactory))
            return heat.Cost;

        return 0.0f;
    }

    /// <summary>
    /// Gets the events that have met their player count, time-until start, etc.
    /// </summary>
    /// <param name="playerCountOverride">Override for player count, if using this to simulate events rather than in an actual round.</param>
    /// <param name="currentTimeOverride">Override for round time, if using this to simulate events rather than in an actual round.</param>
    /// <returns></returns>
    public Dictionary<EntityPrototype, StationEventComponent> AvailableEvents(
        bool ignoreEarliestStart = false,
        int? playerCountOverride = null,
        TimeSpan? currentTimeOverride = null)
    {
        var playerCount = playerCountOverride ?? _playerManager.PlayerCount;

        // playerCount does a lock so we'll just keep the variable here
        var currentTime = currentTimeOverride ?? (!ignoreEarliestStart
            ? GameTicker.RoundDuration()
            : TimeSpan.Zero);

        var result = new Dictionary<EntityPrototype, StationEventComponent>();

        foreach (var (proto, stationEvent) in AllEvents())
        {
            if (CanRun(proto, stationEvent, playerCount, currentTime))
            {
                result.Add(proto, stationEvent);
            }
        }

        return result;
    }

    public Dictionary<EntityPrototype, StationEventComponent> AllEvents()
    {
        var allEvents = new Dictionary<EntityPrototype, StationEventComponent>();
        foreach (var prototype in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract)
                continue;

            if (!prototype.TryGetComponent<StationEventComponent>(out var stationEvent, EntityManager.ComponentFactory))
                continue;

            allEvents.Add(prototype, stationEvent);
        }

        return allEvents;
    }

    private int GetOccurrences(EntityPrototype stationEvent)
    {
        return GetOccurrences(stationEvent.ID);
    }

    private int GetOccurrences(string stationEvent)
    {
        return GameTicker.AllPreviousGameRules.Count(p => p.Item2 == stationEvent);
    }

    public TimeSpan TimeSinceLastEvent(EntityPrototype stationEvent)
    {
        foreach (var (time, rule) in GameTicker.AllPreviousGameRules.Reverse())
        {
            if (rule == stationEvent.ID)
                return time;
        }

        return TimeSpan.Zero;
    }

    private bool CanRun(EntityPrototype prototype, StationEventComponent stationEvent, int playerCount, TimeSpan currentTime)
    {
        if (GameTicker.IsGameRuleActive(prototype.ID))
            return false;

        if (stationEvent.MaxOccurrences.HasValue && GetOccurrences(prototype) >= stationEvent.MaxOccurrences.Value)
        {
            return false;
        }

        if (playerCount < stationEvent.MinimumPlayers)
        {
            return false;
        }

        if (currentTime != TimeSpan.Zero && currentTime.TotalMinutes < stationEvent.EarliestStart)
        {
            return false;
        }

        var lastRun = TimeSinceLastEvent(prototype);
        if (lastRun != TimeSpan.Zero && currentTime.TotalMinutes <
            stationEvent.ReoccurrenceDelay + lastRun.TotalMinutes)
        {
            return false;
        }

        // Frontier: Check max players
        if (playerCount > stationEvent.MaximumPlayers)
        {
            return false;
        }

        // Frontier: require jobs to run event
        foreach (var (jobProtoId, numJobs) in stationEvent.RequiredJobs)
        {
            if (_jobs.GetNumberOfActiveRoles(jobProtoId, false) < numJobs)
                return false;
        }
        // End Frontier

        if (_roundEnd.IsRoundEndRequested() && !stationEvent.OccursDuringRoundEnd)
        {
            return false;
        }

        // Nyano - Summary: - Begin modified code block: check for glimmer events.
        // This could not be cleanly done anywhere else.
        if (_configurationManager.GetCVar(CCVars.GlimmerEnabled) &&
            prototype.TryGetComponent<GlimmerEventComponent>(out var glimmerEvent) &&
            (_glimmerSystem.Glimmer < glimmerEvent.MinimumGlimmer ||
            _glimmerSystem.Glimmer > glimmerEvent.MaximumGlimmer))
        {
            return false;
        }
        // Nyano - End modified code block.

        // Frontier: Check max players
        if (playerCount > stationEvent.MaximumPlayers)
        {
            return false;
        }

        // Frontier: require jobs to run event
        foreach (var (jobProtoId, numJobs) in stationEvent.RequiredJobs)
        {
            if (_jobs.GetNumberOfActiveRoles(jobProtoId, false) < numJobs)
                return false;
        }
        // End Frontier

        if (_roundEnd.IsRoundEndRequested() && !stationEvent.OccursDuringRoundEnd)
        {
            return false;
        }

        return true;
    }
}
