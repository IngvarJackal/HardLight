using Content.Server.Polymorph.Systems;
using Content.Server.Traits;
using Content.Shared.GameTicking;
using Content.Shared._HL.Spawning.Prototypes;
using Content.Shared.Polymorph;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._HL.Spawning.Systems;

public sealed class SpawnCharacterOverrideRuleSystem : EntitySystem
{
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly TraitSystem _traits = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var profileName = args.Profile.Name;
        var currentMob = args.Mob;
        var didOverrideEntity = false;

        foreach (var rule in _prototype.EnumeratePrototypes<CharacterOverrideRulePrototype>())
        {
            if (string.IsNullOrWhiteSpace(rule.Match))
                continue;

            if (!rule.CheckProfileName && !rule.CheckEntityName)
                continue;

            if (!IsWhitelisted(args.Player, rule))
                continue;

            var matches = false;
            if (rule.CheckProfileName && IsMatch(profileName, rule.Match))
                matches = true;

            if (rule.CheckEntityName && !TerminatingOrDeleted(currentMob))
            {
                var entityName = MetaData(currentMob).EntityName;
                if (IsMatch(entityName, rule.Match))
                    matches = true;
            }

            if (!matches)
                continue;

            EntityManager.AddComponents(currentMob, rule.Components, removeExisting: rule.ReplaceExisting);

            if (rule.Entity == null)
                continue;

            var polymorphConfiguration = new PolymorphConfiguration
            {
                Entity = rule.Entity.Value,
                Forced = true,
                TransferDamage = rule.TransferDamage,
                TransferName = rule.TransferName,
                TransferHumanoidAppearance = rule.TransferHumanoidAppearance,
                Inventory = rule.Inventory,
                RevertOnCrit = false,
                RevertOnDeath = false,
                RevertOnEat = false,
                AllowRepeatedMorphs = true,
                PolymorphPopup = null,
                ExitPolymorphPopup = null,
            };

            var newMob = _polymorph.PolymorphEntity(currentMob, polymorphConfiguration);
            if (newMob != null)
            {
                currentMob = newMob.Value;
                didOverrideEntity = true;
            }
        }

        if (didOverrideEntity)
            _traits.ApplyProfileTraits(currentMob, args.Profile, args.Player.Name, addTraitGear: false);
    }

    private static bool IsMatch(string name, string match)
    {
        return string.Equals(name, match, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWhitelisted(ICommonSession player, CharacterOverrideRulePrototype rule)
    {
        var hasLoginWhitelist = rule.Logins.Count > 0;
        var hasUserIdWhitelist = rule.UserIds.Count > 0;

        if (!hasLoginWhitelist && !hasUserIdWhitelist)
            return true;

        if (hasUserIdWhitelist)
        {
            var playerUserId = player.UserId.ToString();
            foreach (var userId in rule.UserIds)
            {
                if (string.Equals(userId, playerUserId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (hasLoginWhitelist)
        {
            var loginComparison = rule.LoginCaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            foreach (var login in rule.Logins)
            {
                if (string.Equals(player.Name, login, loginComparison))
                    return true;
            }
        }

        return false;
    }
}
