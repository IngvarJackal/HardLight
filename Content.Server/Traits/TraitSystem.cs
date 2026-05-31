using Content.Server.Administration.Logs;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems; // HardLight
using Content.Shared.Players;
using Content.Shared.Preferences; // HardLight
using Content.Shared.Roles;
using Content.Shared.Traits;
using Content.Shared.Whitelist;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Linq;
using Content.Server._Starlight.Language; // Starlight

namespace Content.Server.Traits;

public sealed class TraitSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTimeTracking = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly AdminSystem _adminSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly SharedHandsSystem _sharedHandsSystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!; // HardLight

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    // When the player is spawned in, add all trait components selected during character creation
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var pointsTotal = _configuration.GetCVar(CCVars.GameTraitsDefaultPoints);
        var traitSelections = _configuration.GetCVar(CCVars.GameTraitsMax);

        if (args.JobId is not null && _prototype.TryIndex<JobPrototype>(args.JobId, out var jobPrototype)
            && jobPrototype is not null && !jobPrototype.ApplyTraits)
            return;

        var sortedTraits = new List<TraitPrototype>();
        foreach (var traitId in args.Profile.TraitPreferences)
        {
            if (_prototype.TryIndex<TraitPrototype>(traitId, out var traitPrototype))
            {
                sortedTraits.Add(traitPrototype);
            }
            else
            {
                DebugTools.Assert($"No trait found with ID {traitId}!");
                return;
            }
        }

        sortedTraits.Sort();

        foreach (var traitPrototype in sortedTraits)
        {
            // HardLight: skip login-restricted traits for players not in the list
            if (traitPrototype.Logins.Count > 0 && !traitPrototype.Logins.Contains(args.Player.Name))
                continue;

            // Check requirements if they exist
            if (traitPrototype.Requirements.Count > 0)
            {
                // VRS: guard against missing/unset job to avoid InvalidOperationException from First() when no job prototypes are loaded.
                // The original `job` local was unused; we only need to confirm a job is resolvable so requirement checks downstream remain meaningful.
                var hasJob = args.JobId is { } jobId && _prototype.HasIndex<JobPrototype>(jobId);
                if (!hasJob)
                {
                    var any = false;
                    foreach (var _ in _prototype.EnumeratePrototypes<JobPrototype>())
                    {
                        any = true;
                        break;
                    }
                    if (!any)
                    {
                        DebugTools.Assert("TraitSystem: no JobPrototype available to evaluate trait requirements.");
                        continue;
                    }
                }

                var playTimes = _playTimeTracking.GetTrackerTimes(args.Player);

                var requirementsMet = true;
                foreach (var requirement in traitPrototype.Requirements)
                {
                    if (!requirement.Check(EntityManager, _prototype, args.Profile, playTimes, out _))
                    {
                        requirementsMet = false;
                        break;
                    }
                }

                if (!requirementsMet)
                    continue;
            }

            // To check for cheaters
            pointsTotal -= traitPrototype.Cost;
            --traitSelections;

            AddTrait(args.Mob, traitPrototype);
        }

        if (pointsTotal < 0 || traitSelections < 0)
            PunishCheater(args.Mob);
    }

    /// <summary>
    /// HardLight: Applies the selected traits from a humanoid profile to an existing entity.
    /// This is intended for non-standard spawn paths like admin spawning or cloning
    /// that already have a validated profile and just need its trait components replayed.
    /// </summary>
    public void ApplyProfileTraits(EntityUid uid, HumanoidCharacterProfile profile, string? playerName = null, bool addTraitGear = true)
    {
        var sortedTraits = new List<TraitPrototype>();
        foreach (var traitId in profile.TraitPreferences)
        {
            if (_prototype.TryIndex<TraitPrototype>(traitId, out var traitPrototype))
                sortedTraits.Add(traitPrototype);
        }

        sortedTraits.Sort();

        foreach (var traitPrototype in sortedTraits)
        {
            if (traitPrototype.Logins.Count > 0 &&
                (playerName == null || !traitPrototype.Logins.Contains(playerName)))
            {
                continue;
            }

            AddTrait(uid, traitPrototype, addTraitGear);
        }
    }

    /// <summary>
    ///     Adds a single Trait Prototype to an Entity.
    /// </summary>
    public void AddTrait(EntityUid uid, TraitPrototype traitPrototype, bool addTraitGear = true) // HardLight: Added bool addTraitGear
    {
        // Check whitelist/blacklist
        if (_whitelistSystem.IsWhitelistFail(traitPrototype.Whitelist, uid) ||
            _whitelistSystem.IsBlacklistPass(traitPrototype.Blacklist, uid))
            return;

        // Add all components required by the prototype
        EntityManager.AddComponents(uid, traitPrototype.Components, traitPrototype.ReplaceComponents); // Hardlight: Added ReplaceComponents

            // Starlight start
            var language = EntityManager.System<LanguageSystem>();

            if (traitPrototype.RemoveLanguagesSpoken is not null)
                foreach (var lang in traitPrototype.RemoveLanguagesSpoken)
                    language.RemoveLanguage(uid, lang, true, false); // HardLight: args.Mob<uid

            if (traitPrototype.RemoveLanguagesUnderstood is not null)
                foreach (var lang in traitPrototype.RemoveLanguagesUnderstood)
                    language.RemoveLanguage(uid, lang, false, true); // HardLight: args.Mob<uid

            if (traitPrototype.LanguagesSpoken is not null)
                foreach (var lang in traitPrototype.LanguagesSpoken)
                    language.AddLanguage(uid, lang, true, false); // HardLight: args.Mob<uid

            if (traitPrototype.LanguagesUnderstood is not null)
                foreach (var lang in traitPrototype.LanguagesUnderstood)
                    language.AddLanguage(uid, lang, false, true); // HardLight: args.Mob<uid
            // Starlight end

        // HardLight: Force an immediate refresh so movement penalties/bonuses apply on spawn.
        _movementSpeed.RefreshMovementSpeedModifiers(uid);

        // Add item required by the trait
        if (addTraitGear && traitPrototype.TraitGear != null && TryComp(uid, out HandsComponent? handsComponent)) // HardLight: Added addTraitGear
        {
            var coords = Transform(uid).Coordinates;
            var inhandEntity = EntityManager.SpawnEntity(traitPrototype.TraitGear, coords);
            _sharedHandsSystem.TryPickup(uid,
                inhandEntity,
                checkActionBlocker: false,
                handsComp: handsComponent);
        }
    }

    /// <summary>
    ///     On a non-cheating client, it is not possible to save a character with a negative number of traits. This can however
    ///     trigger incorrectly if a character was saved, and then at a later point in time an admin changes the traits Cvars to reduce the points.
    ///     Or if the points costs of traits is increased.
    /// </summary>
    private void PunishCheater(EntityUid uid)
    {
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(uid):entity} attempted to spawn with an invalid trait list. This might be a mistake, or they might be cheating");

        if (!_configuration.GetCVar(CCVars.TraitsPunishCheaters)
            || !_playerManager.TryGetSessionByEntity(uid, out var targetPlayer))
            return;

        // For maximum comedic effect, this is plenty of time for the cheater to get on station and start interacting with people.
        var timeToDestroy = _random.NextFloat(120, 360);

        Timer.Spawn(TimeSpan.FromSeconds(timeToDestroy), () => VaporizeCheater(targetPlayer));
    }

    /// <summary>
    ///     https://www.youtube.com/watch?v=X2QMN0a_TrA
    /// </summary>
    private void VaporizeCheater(ICommonSession targetPlayer)
    {
        _adminSystem.Erase(targetPlayer.UserId);

        var feedbackMessage = "[font size=24][color=#ff0000]You have spawned in with an illegal trait point total. If this was a result of cheats, then your nonexistence is a skill issue. Otherwise, feel free to click 'Return To Lobby', and fix your trait selections.[/color][/font]";
        _chatManager.ChatMessageToOne(
            ChatChannel.Server,
            feedbackMessage,
            feedbackMessage,
            EntityUid.Invalid,
            false,
            targetPlayer.Channel);
    }
}
