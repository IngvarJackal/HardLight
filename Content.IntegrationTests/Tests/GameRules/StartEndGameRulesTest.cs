using System.Linq;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using System.Collections;
using System.Collections.Generic;

namespace Content.IntegrationTests.Tests.GameRules;

[TestFixture]
public sealed class StartEndGameRulesTest
{
    /// <summary>
    ///     Tests that all game rules can be added/started/ended at the same time without exceptions.
    /// </summary>
    [Test]
    public async Task TestAllConcurrent()
    {
        var eventWhitelist = new List<string> {
            "LizardVents" // HL: the lizards anger the tests when they're destroyed
        };
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = true,
            Fresh = true,
            Destructive = true
        });
        var server = pair.Server;
        await server.WaitIdleAsync();
        var gameTicker = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<GameTicker>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        Assert.That(cfg.GetCVar(CCVars.GridFill), Is.False);
        await server.WaitRunTicks(pair.SecondsToTicks(10));

        await server.WaitAssertion(() =>
        {
            var rules = gameTicker.GetAllGameRulePrototypes().ToList();
            rules.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));

            // Start all rules
            foreach (var rule in rules)
            {
                if (!eventWhitelist.Contains(rule.ID))
                    gameTicker.StartGameRule(rule.ID);
            }
        });

        // Wait 150 ticks for any random update loops that might happen
        await server.WaitRunTicks(150); // HL: Up from 3 ticks to 150 because we have a lot more stuff going on at round start

        await server.WaitAssertion(() =>
        {
            // End all rules
            gameTicker.ClearGameRules();
            Assert.That(!gameTicker.GetAddedGameRules().Any());
        });
        await server.WaitRunTicks(10); // HL: The cleanup takes some time for some reason

        await pair.CleanReturnAsync();
    }
}
