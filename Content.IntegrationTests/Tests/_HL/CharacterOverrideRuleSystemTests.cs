using Content.IntegrationTests.Pair;
using Content.Server._HL.Spawning.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared.CCVar;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._HL;

[TestFixture]
[TestOf(typeof(SpawnCharacterOverrideRuleSystem))]
public sealed class CharacterOverrideRuleSystemTests
{
    private const string TestMapId = "CharacterOverrideRuleTestMap";
    private const string OverrideEntityId = "CharacterOverrideRuleTestMob";
    private const string TraitId = "CharacterOverrideRuleTestTrait";
    private const string MatchingProfileName = "Character Override Trait Test";

    [TestPrototypes]
    private const string Prototypes = @"
- type: gameMap
  id: CharacterOverrideRuleTestMap
  mapName: CharacterOverrideRuleTestMap
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
            Contractor: [ -1, -1 ]

- type: entity
  id: CharacterOverrideRuleTestMob
  parent: MobHuman
  name: Character Override Rule Test Mob

- type: trait
  id: CharacterOverrideRuleTestTrait
  name: trait-synthetic-name
  description: trait-synthetic-desc
  components:
    - type: Pacified

- type: spawnCharacterOverrideRule
  id: CharacterOverrideRuleTestRule
  match: Character Override Trait Test
  checkProfileName: true
  checkEntityName: false
  entity: CharacterOverrideRuleTestMob
  transferHumanoidAppearance: true
";

    [Test]
    public async Task EntityOverrideReappliesSelectedProfileTraitsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var prefMan = server.ResolveDependency<IServerPreferencesManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var userId = pair.Client.User!.Value;

        server.CfgMan.SetCVar(CCVars.GameMap, TestMapId);

        var prefs = prefMan.GetPreferences(userId);
        Assert.That(prefs.SelectedCharacterIndex, Is.EqualTo(0));

        var selected = (HumanoidCharacterProfile) prefs.Characters[0];
        var profile = selected
            .WithName(MatchingProfileName)
            .WithTraitPreference(TraitId, server.ProtoMan);

        await server.WaitPost(() => prefMan.SetProfile(userId, 0, profile).Wait());

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.PlayerGameStatuses[userId], Is.EqualTo(PlayerGameStatus.JoinedGame));

            var attached = playerMan.GetSessionById(userId).AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var meta = server.EntMan.GetComponent<MetaDataComponent>(attached!.Value);
            Assert.That(meta.EntityPrototype?.ID, Is.EqualTo(OverrideEntityId));
            Assert.That(server.EntMan.HasComponent<PacifiedComponent>(attached.Value), Is.True);
        });

        await server.WaitPost(() => ticker.RestartRound());
        await pair.CleanReturnAsync();
    }
}