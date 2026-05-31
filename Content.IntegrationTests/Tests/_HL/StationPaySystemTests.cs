using System;
using Content.Server.GameTicking;
using Content.Server._Hardlight.StationPay;
using Content.Shared.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._HL;

[TestFixture]
[TestOf(typeof(StationPaySystem))]
public sealed class StationPaySystemTests
{
    private static readonly ProtoId<JobPrototype> PaidJob = "Prisoner";
    private const string TestMapId = "StationPayTestMap";

    [TestPrototypes]
    private const string Prototypes = @"
- type: gameMap
  id: StationPayTestMap
  mapName: StationPayTestMap
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            Prisoner: [ -1, -1 ]
";

    [Test]
    public async Task StationJobPaysDuringRoundTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var userId = pair.Client.User!.Value;

        server.CfgMan.SetCVar(CCVars.GameMap, TestMapId);
        server.CfgMan.SetCVar(CCVars.GameStationPayoutDelay, TimeSpan.FromSeconds(1));

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        EntityUid attached = default;
        int initialBalance = 0;

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.PlayerGameStatuses[userId], Is.EqualTo(PlayerGameStatus.JoinedGame));

            var session = playerMan.GetSessionById(userId);
            Assert.That(session.AttachedEntity, Is.Not.Null);

            attached = session.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<BankAccountComponent>(attached, out var account), Is.True);
            initialBalance = account!.Balance;
        });

        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

            var account = server.EntMan.GetComponent<BankAccountComponent>(attached);
            Assert.That(account.Balance, Is.GreaterThan(initialBalance), "Expected station pay to deposit during the round once the payout delay elapsed.");
        });

        await server.WaitPost(() => ticker.RestartRound());
        await pair.CleanReturnAsync();

    }
}