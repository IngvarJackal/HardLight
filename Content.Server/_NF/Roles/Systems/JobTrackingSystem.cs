using Content.Server._NF.CryoSleep;
using Content.Server._HL.ColComm; // HardLight
using Content.Server.Afk;
using Content.Server.GameTicking;
using Content.Server._NF.RoundNotifications.Events; // HardLight
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking; // HardLight
using Content.Shared.Mind; // HardLight
using Content.Shared._NF.Roles.Components;
using Content.Shared._NF.Roles.Systems;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network; // HardLight
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Roles.Systems;

/// HardLight start: Rewritten
/// <summary>
/// Handles job slot open/close lifecycle for tracked station jobs.
/// All slot operations are routed through the persistent
/// <see cref="ColcommJobRegistryComponent"/> on the ColComm grid entity,
/// which survives round transitions and avoids stale EntityUid issues.
/// </summary>
// HardLight end
public sealed class JobTrackingSystem : SharedJobTrackingSystem
{
    [Dependency] private readonly IAfkManager _afk = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly ColcommJobSystem _colcommJobs = default!; // HardLight

    // HardLight: Round restart deletes all station entities as part of cleanup.
    // Those deletions should not be treated like a mid-round ship sale/destruction,
    // or we will mark persisted crew inactive before the next round's slot accounting runs.
    private bool _suppressStationTerminationRelease;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JobTrackingComponent, CryosleepBeforeMindRemovedEvent>(OnJobBeforeCryoEntered);
        SubscribeLocalEvent<JobTrackingComponent, MindAddedMessage>(OnJobMindAdded);
        SubscribeLocalEvent<JobTrackingComponent, MindRemovedMessage>(OnJobMindRemoved);
        SubscribeLocalEvent<JobTrackingComponent, EntityTerminatingEvent>(OnTrackedJobTerminating); // HardLight
        SubscribeLocalEvent<ColcommRegistryRoundStartEvent>(OnColcommRegistryRoundStart); // HardLight
        SubscribeLocalEvent<StationInitializedEvent>(OnStationInitialized); // HardLight
        SubscribeLocalEvent<StationJobsComponent, EntityTerminatingEvent>(OnStationJobsTerminating); // HardLight
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup); // HardLight
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted); // HardLight
    }

    /// <summary>
    /// HardLight: When a station is destroyed mid-round (ship sold/destroyed), release any
    /// ColComm slots still attributed to crew whose <see cref="JobTrackingComponent.SpawnStation"/>
    /// pointed at it. Without this, players whose body survives station deletion (e.g. transferred
    /// to another grid, ghosted later, or in a shuttle that left) keep their ColComm slot held
    /// until their body itself dies/cryos. Marks each tracked component inactive so its later
    /// MindRemovedMessage does not double-refund.
    /// </summary>
    private void OnStationJobsTerminating(Entity<StationJobsComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_suppressStationTerminationRelease) // HardLight
            return;

        var query = AllEntityQuery<JobTrackingComponent>();
        while (query.MoveNext(out var bodyUid, out var tracking))
        {
            if (!tracking.Active || tracking.SpawnStation != ent.Owner)
                continue;

            OpenJob((bodyUid, tracking));
        }
    }

    /// <summary>
    /// HardLight: After the ColComm registry resets to defaults, deduct slots for all crew
    /// that persisted from the previous round (Active = true in their JobTrackingComponent).
    /// </summary>
    private void OnColcommRegistryRoundStart(ColcommRegistryRoundStartEvent ev)
    {
        var activeCounts = new Dictionary<ProtoId<JobPrototype>, int>();

        var jobQuery = AllEntityQuery<JobTrackingComponent>();
        while (jobQuery.MoveNext(out _, out var job))
        {
            if (!job.Active || job.Job is not { } jobId)
                continue;

            var colcommJobId = _stationJobs.GetColcommJobId(jobId);
            activeCounts.TryGetValue(colcommJobId, out var existing);
            activeCounts[colcommJobId] = existing + 1;
        }

        if (activeCounts.Count > 0)
            _colcommJobs.DeductActiveRoles(ev.Colcomm, activeCounts);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev) // HardLight
    {
        _suppressStationTerminationRelease = true;
    }

    private void OnRoundStarted(RoundStartedEvent ev) // HardLight
    {
        _suppressStationTerminationRelease = false;
        RebindCarriedOverTrackedJobs();
    }

    private void OnTrackedJobTerminating(Entity<JobTrackingComponent> ent, ref EntityTerminatingEvent args) // HardLight
    {
        if (!ent.Comp.Active || ent.Comp.Job == null)
            return;

        OpenJob(ent);
    }

    // HardLight: When a new station finishes initializing after a round transition, rebind any
    // active tracked jobs that still point at a deleted prior-round station to this live station.
    private void OnStationInitialized(StationInitializedEvent ev)
    {
        if (!TryComp<StationJobsComponent>(ev.Station, out var stationJobs))
            return;

        var query = AllEntityQuery<JobTrackingComponent>();
        while (query.MoveNext(out var uid, out var tracking))
        {
            if (!tracking.Active
                || tracking.Job is not { } job
                || (!Deleted(tracking.SpawnStation) && HasComp<StationJobsComponent>(tracking.SpawnStation)))
            {
                continue;
            }

            var stationJob = _stationJobs.GetStationTrackingJobId(ev.Station, job, stationJobs);
            if (!_stationJobs.IsConfiguredJob(ev.Station, stationJob, stationJobs))
                continue;

            tracking.SpawnStation = ev.Station;
            Dirty(uid, tracking);
        }
    }

    // HardLight: On round start, normalize carried-over tracked jobs onto a live configured
    // station so later close/open operations do not keep referencing deleted round entities.
    private void RebindCarriedOverTrackedJobs()
    {
        var query = AllEntityQuery<JobTrackingComponent>();
        while (query.MoveNext(out var uid, out var tracking))
        {
            if (!tracking.Active || tracking.Job is not { } job)
                continue;

            var reboundStation = ResolveTrackedStation(tracking.SpawnStation, job);
            if (reboundStation == tracking.SpawnStation)
                continue;

            tracking.SpawnStation = reboundStation;
            Dirty(uid, tracking);
        }
    }

    // HardLight: Resolve the live station that should own a carried-over tracked job when its
    // original station entity no longer exists after restart cleanup.
    private EntityUid ResolveTrackedStation(EntityUid spawnStation, ProtoId<JobPrototype> job)
    {
        if (!Deleted(spawnStation) && HasComp<StationJobsComponent>(spawnStation))
            return spawnStation;

        EntityUid? defaultMapStation = null;
        EntityUid? nonShipStation = null;
        EntityUid? fallbackStation = null;
        var stationQuery = EntityQueryEnumerator<StationJobsComponent>();
        while (stationQuery.MoveNext(out var stationUid, out var stationJobs))
        {
            var stationJob = _stationJobs.GetStationTrackingJobId(stationUid, job, stationJobs);
            if (!_stationJobs.IsConfiguredJob(stationUid, stationJob, stationJobs))
                continue;

            if (IsStationOnDefaultMap(stationUid))
                defaultMapStation = stationUid;

            if (!_stationJobs.IsShipCrewHiringStation(stationUid))
                nonShipStation = stationUid;

            fallbackStation = stationUid;
        }

        return defaultMapStation
            ?? nonShipStation
            ?? fallbackStation
            ?? spawnStation;
    }

    private bool IsStationOnDefaultMap(EntityUid stationUid) // HardLight
    {
        if (!TryComp<StationDataComponent>(stationUid, out var stationData))
            return false;

        foreach (var gridUid in stationData.Grids)
        {
            if (Deleted(gridUid) || !TryComp<TransformComponent>(gridUid, out var xform))
                continue;

            if (xform.MapID == _gameTicker.DefaultMap)
                return true;
        }

        return false;
    }

    private List<(EntityUid Station, StationJobsComponent Jobs, ProtoId<JobPrototype> StationJob)> GetMatchingTrackedStations(ProtoId<JobPrototype> job) // HardLight
    {
        var stations = new List<(EntityUid, StationJobsComponent, ProtoId<JobPrototype>)>();
        var query = EntityQueryEnumerator<StationJobsComponent>();
        while (query.MoveNext(out var stationUid, out var stationJobs))
        {
            var stationJob = _stationJobs.GetStationTrackingJobId(stationUid, job, stationJobs);
            if (!_stationJobs.IsConfiguredJob(stationUid, stationJob, stationJobs))
                continue;

            stations.Add((stationUid, stationJobs, stationJob));
        }

        return stations;
    }

    // HardLight: If a player returns to their body (or an admin forces a mind in), consume a
    // ColComm slot unless we already track them.
    private void OnJobMindAdded(Entity<JobTrackingComponent> ent, ref MindAddedMessage ev)
    {
        // If the job is null, don't do anything.
        if (ent.Comp.Job is not { } job)
            return;

        if (!ent.Comp.Active)
        {
            ent.Comp.Active = true;
            RaiseLocalEvent(new JobTrackingStateChangedEvent());
        }

        if (!ShouldReopenTrackedJob(ent.Comp.SpawnStation, job))
            return;

        if (!_player.TryGetSessionByEntity(ent, out var session))
            return;

        CloseJob(ent, session.UserId);
    }

    private void OnJobMindRemoved(Entity<JobTrackingComponent> ent, ref MindRemovedMessage ev)
    {
        if (ent.Comp.Job == null || !ent.Comp.Active || !ShouldReopenTrackedJob(ent.Comp.SpawnStation, ent.Comp.Job.Value))
            return;

        OpenJob(ent, ev.Mind.Comp.UserId); // HardLight: Added ev.Mind.Comp.UserId
    }

    private void OnJobBeforeCryoEntered(Entity<JobTrackingComponent> ent, ref CryosleepBeforeMindRemovedEvent ev)
    {
        if (ent.Comp.Job == null || !ent.Comp.Active || !ShouldReopenTrackedJob(ent.Comp.SpawnStation, ent.Comp.Job.Value))
            return;

        OpenJob(ent, ev.User); // HardLight: Added ev.User
    }

    public void OpenJob(Entity<JobTrackingComponent> ent, NetUserId? userId = null) // HardLight: Added NetUserId? userId = null
    {
        if (ent.Comp.Job is not { } job)
            return;

        // HardLight: idempotent — if the slot was already opened (e.g. by station deletion sweep
        // or a prior disconnect grace release), don't refund again.
        if (!ent.Comp.Active)
            return;

        ent.Comp.Active = false;
        RaiseLocalEvent(new JobTrackingStateChangedEvent()); // HardLight

        // HardLight start
        var originalSpawnStation = ent.Comp.SpawnStation;
        StationJobsComponent? originalStationJobs = null;
        var hadLiveSpawnStation = !Deleted(originalSpawnStation)
            && TryComp(originalSpawnStation, out originalStationJobs);

        var spawnStation = ResolveTrackedStation(originalSpawnStation, job);
        if (spawnStation != ent.Comp.SpawnStation)
        {
            ent.Comp.SpawnStation = spawnStation;
            Dirty(ent, ent.Comp);
        }

        var stationTargets = new List<(EntityUid Station, StationJobsComponent Jobs, ProtoId<JobPrototype> StationJob)>();
        if (hadLiveSpawnStation)
        {
            stationTargets.Add((originalSpawnStation, originalStationJobs!, _stationJobs.GetStationTrackingJobId(originalSpawnStation, job, originalStationJobs)));
        }
        else
        {
            stationTargets = GetMatchingTrackedStations(job);
        }
        // HardLight end

        NetUserId? trackedUserId = userId;
        if (trackedUserId == null && _player.TryGetSessionByEntity(ent, out var session))
            trackedUserId = session.UserId;

        // HardLight start
        if (trackedUserId == null
            && TryComp<MindContainerComponent>(ent, out var mindContainer)
            && mindContainer.Mind is { } mindUid
            && TryComp<MindComponent>(mindUid, out var mind)
            && mind.UserId is { } mindUserId)
        {
            trackedUserId = mindUserId;
        }

        foreach (var (stationUid, jobs, stationJob) in stationTargets)
        {
            if (trackedUserId != null)
                _stationJobs.TryUntrackPlayerJob(stationUid, trackedUserId.Value, stationJob, jobs);

            _stationJobs.TryReopenTrackedJobSlot(stationUid, stationJob, jobs);
        }
        // HardLight end

        var colcommJob = _stationJobs.GetColcommJobId(job);

        if (_colcommJobs.TryGetColcommRegistry(out var colcomm)
            && _colcommJobs.TryGetJobSlot(colcomm, colcommJob, out var slots)
            && slots != null)
        {
            // Only reopen the global pool if it has spare capacity for this role.
            var occupiedJobs = GetNumberOfActiveColcommRoles(colcommJob, includeAfk: true, exclude: ent, includeOutsideDefaultMap: true);
            var midRoundMax = colcomm.Comp.MidRoundMaxSlots.GetValueOrDefault(colcommJob, 0);

            if (slots + occupiedJobs < midRoundMax)
                _colcommJobs.TryAdjustJobSlot(colcomm, colcommJob, 1);

            if (trackedUserId != null)
                _colcommJobs.TryUntrackPlayerJob(colcomm, trackedUserId.Value, colcommJob);
        }
    }

    public void EnsureTrackedJob(EntityUid uid, ProtoId<JobPrototype> jobId, EntityUid spawnStation, bool active = true)
    {
        var jobComp = EnsureComp<JobTrackingComponent>(uid);
        jobComp.Job = jobId;
        jobComp.SpawnStation = spawnStation;
        jobComp.Active = active;
        Dirty(uid, jobComp);
    }

    // HardLight: CloseJob consumes a reopened slot and re-tracks the player in ColComm/station job registries.
    private void CloseJob(Entity<JobTrackingComponent> ent, NetUserId userId)
    {
        if (ent.Comp.Job is not { } job)
            return;

        if (!ent.Comp.Active)
        {
            ent.Comp.Active = true;
            RaiseLocalEvent(new JobTrackingStateChangedEvent());
        }

        var spawnStation = ResolveTrackedStation(ent.Comp.SpawnStation, job);
        if (spawnStation != ent.Comp.SpawnStation)
        {
            ent.Comp.SpawnStation = spawnStation;
            Dirty(ent, ent.Comp);
        }

        if (!ShouldReopenTrackedJob(spawnStation, job))
            return;

        var stationJob = _stationJobs.GetStationTrackingJobId(spawnStation, job);

        if (TryComp<StationJobsComponent>(spawnStation, out var stationJobs)
            && !_stationJobs.IsPlayerJobTracked(spawnStation, userId, stationJob, stationJobs))
        {
            if (_stationJobs.TryGetJobSlot(spawnStation, stationJob, out var localSlots) && localSlots > 0)
                _stationJobs.TryAdjustJobSlot(spawnStation, stationJob, -1, clamp: true, stationJobs: stationJobs);

            _stationJobs.TryTrackPlayerJob(spawnStation, userId, stationJob, stationJobs);
        }

        var colcommJob = _stationJobs.GetColcommJobId(job);

        if (!_colcommJobs.TryGetColcommRegistry(out var colcomm)
            || !_colcommJobs.TryGetJobSlot(colcomm, colcommJob, out var slots)
            || _colcommJobs.IsPlayerJobTracked(colcomm, userId, colcommJob))
        {
            return;
        }

        if (slots > 0)
            _colcommJobs.TryAdjustJobSlot(colcomm, colcommJob, -1, clamp: true);

        _colcommJobs.TryTrackPlayerJob(colcomm, userId, colcommJob);
    }

    /// <summary>
    /// VRS: Move a <see cref="JobTrackingComponent"/> from one body to another so a subsequent
    /// mind transfer between them does not trigger the slot-reopen pathway. Used by the
    /// AI / borg shunt flow (<c>PositronicJumpSystem</c>) to fix HardLight issue #1354 where
    /// the AI job slot reopened to new joiners as soon as the AI shunted into a chassis.
    /// </summary>
    /// <remarks>
    /// The destination's <see cref="MindAddedMessage"/> still runs <see cref="OnJobMindAdded"/>
    /// and falls through to <see cref="CloseJob"/>, but <c>CloseJob</c>'s
    /// <c>IsPlayerJobTracked</c> guard short-circuits the actual slot decrement because the
    /// player is already tracked against this slot from their original spawn.
    /// </remarks>
    public void MoveTrackingFromTo(EntityUid from, EntityUid to)
    {
        if (!TryComp<JobTrackingComponent>(from, out var source))
            return;

        // Suppress OnJobMindRemoved on the source: it early-returns when Active is false.
        source.Active = false;

        var dest = EnsureComp<JobTrackingComponent>(to);
        dest.Job = source.Job;
        dest.SpawnStation = source.SpawnStation;
        // Mark Active so the destination's OnJobMindAdded skips the activation branch and
        // CloseJob reaches its IsPlayerJobTracked no-op path instead of trying to consume
        // a slot that was already consumed at the original spawn.
        dest.Active = true;

        RemComp<JobTrackingComponent>(from);
    }

    private bool ShouldReopenTrackedJob(EntityUid spawnStation, ProtoId<JobPrototype> job)
    {
        if (JobShouldBeReopened(job))
            return true;

        return _stationJobs.GetStationTrackingJobId(spawnStation, job) != job;
    }

    private int GetNumberOfActiveColcommRoles(
        ProtoId<JobPrototype> colcommJobId,
        bool includeAfk = true,
        EntityUid? exclude = null,
        bool includeOutsideDefaultMap = false)
    {
        var activeJobCount = 0;
        var jobQuery = AllEntityQuery<JobTrackingComponent, MindContainerComponent, TransformComponent>();
        while (jobQuery.MoveNext(out var uid, out var job, out _, out var xform))
        {
            if (exclude == uid)
                continue;

            if (!job.Active
                || job.Job is not { } trackedJob
                || _stationJobs.GetColcommJobId(trackedJob) != colcommJobId
                || (!includeOutsideDefaultMap && xform.MapID != _gameTicker.DefaultMap))
                continue;

            if (_player.TryGetSessionByEntity(uid, out var session))
            {
                if (session.State.Status != SessionStatus.InGame)
                    continue;

                if (!includeAfk && _afk.IsAfk(session))
                    continue;
            }

            activeJobCount++;
        }

        return activeJobCount;
    }

    /// <summary>
    /// Returns the number of active players who match the requested Job Prototype Id.
    /// </summary>
    // HardLight start
    public int GetNumberOfActiveRoles(
        ProtoId<JobPrototype> jobProtoId,
        bool includeAfk = true,
        EntityUid? exclude = null,
        bool includeOutsideDefaultMap = false)
    // HardLight end
    {
        var activeJobCount = 0;
        var jobQuery = AllEntityQuery<JobTrackingComponent, MindContainerComponent, TransformComponent>();
        while (jobQuery.MoveNext(out var uid, out var job, out _, out var xform)) // HardLight: out var mindContainer<out _
        {
            if (exclude == uid)
                continue;

            if (!job.Active
                || job.Job != jobProtoId
                || (!includeOutsideDefaultMap && xform.MapID != _gameTicker.DefaultMap)) // Skip if they're in cryo or on expedition, // HardLight: Added !includeOutsideDefaultMap
                continue;

            if (_player.TryGetSessionByEntity(uid, out var session))
            {
                if (session.State.Status != SessionStatus.InGame)
                    continue;

                if (!includeAfk && _afk.IsAfk(session))
                    continue;
            }

            activeJobCount++;
        }
        return activeJobCount;
    }
}

// HardLight: An event raised when a job tracking component's active state changes, used for dynamic job allocation rules.
public sealed class JobTrackingStateChangedEvent : EntityEventArgs
{
}
