#nullable enable
using Content.Shared.CCVar;
using Content.Shared.HL.CCVar;

namespace Content.IntegrationTests;

// Partial class containing test cvars
// This could probably be merged into the main file, but I'm keeping it separate to reduce
// conflicts for forks.
public static partial class PoolManager
{
    public static readonly (string cvar, string value)[] TestCvars =
    {
        // @formatter:off
        (CCVars.DatabaseSynchronous.Name,     "true"),
        (CCVars.DatabaseSqliteDelay.Name,     "0"),
        (CCVars.HolidaysEnabled.Name,         "false"),
        (CCVars.GameMap.Name,                 TestMap),
        (CCVars.AdminLogsQueueSendDelay.Name, "0"),
        (CCVars.NPCMaxUpdates.Name,           "999999"),
        (CCVars.GameRoleTimers.Name,          "false"),
        (CCVars.GameRoleWhitelist.Name,       "false"),
        (CCVars.GridFill.Name,                "false"),
        (CCVars.PreloadGrids.Name,            "false"),
        (CCVars.ArrivalsShuttles.Name,        "false"),
        (CCVars.EmergencyShuttleEnabled.Name, "false"),
        (CCVars.ProcgenPreload.Name,          "false"),
        (CCVars.WorldgenEnabled.Name,         "false"),
        (CCVars.GatewayGeneratorEnabled.Name, "false"),
        (CCVars.GameDummyTicker.Name, "true"),
        (CCVars.GameLobbyEnabled.Name, "false"),
        (CCVars.ConfigPresetDevelopment.Name, "false"),
        (CCVars.AdminLogsEnabled.Name, "false"),
        (CCVars.AutosaveEnabled.Name, "false"),
        (CCVars.InteractionRateLimitCount.Name, "9999999"),
        (CCVars.InteractionRateLimitPeriod.Name, "0.1"),
        (CCVars.MovementMobPushing.Name, "false"),
        (CCVars.GameLobbyDefaultPreset.Name, "nftest"), // Frontier: Adventure takes ages, default to nftest (no need to test events we will not run, e.g. meteor swarm)
        (CCVars.StaticStorageUI.Name, "true"), // Frontier: causes storage test failures
        (CCVars.StorageLimit.Name, "1"),// Frontier: test failures with multiple storage enabled
        (CCVars.AutoVoteEnabled.Name,         "false"), // HL: Stop auto-vote from starting a round mid-test
        (HLCCVars.RoundPersistenceEnabled.Name,      "false"), // HL: Stop things persisting between rounds for testing
        (CCVars.EventsEnabled.Name, "false"), // HL: We don't want random events messing with tests
        (HLCCVars.AutoSpawnColComm.Name, "false"), // HL: colcomm spawning fucks all the tests that look at ent counts and I spent way too long figuring out how to turn it off
        (CCVars.VoteTimerRestart.Name, "120"),
        (CCVars.VoteTimerPreset.Name, "120"),
        (CCVars.VoteTimerMap.Name, "120"),
        (CCVars.VoteTimerAlone.Name, "120"),
        (CCVars.RoundRestartTime.Name, "1200")
    };
}
