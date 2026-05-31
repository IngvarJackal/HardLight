using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Contraband;
using Content.Shared.Inventory;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Containers;
using System.Collections.Frozen;
using System.Linq;

namespace Content.Shared.NPC.Systems;

/// <summary>
///     Outlines faction relationships with each other.
/// </summary>
public sealed partial class NpcFactionSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private static readonly ProtoId<NpcFactionPrototype> ContrabandDetectionFaction = "ContrabandDetection";
    private static readonly HashSet<ProtoId<ContrabandSeverityPrototype>> Class3EquivalentContrabandSeverities =
    [
        "OtherFactionGear2",
        "OtherFactionGear3",
    ];

    private static readonly HashSet<ProtoId<AccessLevelPrototype>> ContrabandDetectionSecurityExemptions =
    [
        "StationCaptain",
        "HeadOfPersonnel",
        "ChiefEngineer",
        "ChiefMedicalOfficer",
        "HeadOfSecurity",
        "Quartermaster",
        "ResearchDirector",
        "Security",
        "Armory",
        "Brig",
        "Detective",
    ];

    /// <summary>
    /// To avoid prototype mutability we store an intermediary data class that gets used instead.
    /// </summary>
    private FrozenDictionary<string, FactionData> _factions = FrozenDictionary<string, FactionData>.Empty;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcFactionMemberComponent, ComponentStartup>(OnFactionStartup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnProtoReload);

        InitializeException();
        RefreshFactions();
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (obj.WasModified<NpcFactionPrototype>())
            RefreshFactions();
    }

    private void OnFactionStartup(Entity<NpcFactionMemberComponent> ent, ref ComponentStartup args)
    {
        RefreshFactions(ent);
    }

    /// <summary>
    /// Refreshes the cached factions for this component.
    /// </summary>
    private void RefreshFactions(Entity<NpcFactionMemberComponent> ent)
    {
        ent.Comp.FriendlyFactions.Clear();
        ent.Comp.HostileFactions.Clear();

        foreach (var faction in ent.Comp.Factions)
        {
            // YAML Linter already yells about this, don't need to log an error here
            if (!_factions.TryGetValue(faction, out var factionData))
                continue;

            ent.Comp.FriendlyFactions.UnionWith(factionData.Friendly);
            ent.Comp.HostileFactions.UnionWith(factionData.Hostile);
        }
        // Add additional factions if it is written in prototype
        if (ent.Comp.AddFriendlyFactions != null)
        {
            ent.Comp.FriendlyFactions.UnionWith(ent.Comp.AddFriendlyFactions);
        }
        if (ent.Comp.AddHostileFactions != null)
        {
            ent.Comp.HostileFactions.UnionWith(ent.Comp.AddHostileFactions);
        }
    }

    /// <summary>
    /// Returns whether an entity is a member of a faction.
    /// </summary>
    public bool IsMember(Entity<NpcFactionMemberComponent?> ent, string faction)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        return ent.Comp.Factions.Contains(faction);
    }

    /// <summary>
    /// Returns whether an entity is a member of any listed faction.
    /// If the list is empty this returns false.
    /// </summary>
    public bool IsMemberOfAny(Entity<NpcFactionMemberComponent?> ent, IEnumerable<ProtoId<NpcFactionPrototype>> factions)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        foreach (var faction in factions)
        {
            if (ent.Comp.Factions.Contains(faction))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether both entities share at least one direct faction membership.
    /// </summary>
    public bool SharesAnyFaction(Entity<NpcFactionMemberComponent?> ent, Entity<NpcFactionMemberComponent?> other)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !Resolve(other, ref other.Comp, false))
            return false;

        return ent.Comp.Factions.Overlaps(other.Comp.Factions);
    }

    /// <summary>
    /// Adds this entity to the particular faction.
    /// </summary>
    public void AddFaction(Entity<NpcFactionMemberComponent?> ent, string faction, bool dirty = true)
    {
        if (!_proto.HasIndex<NpcFactionPrototype>(faction))
        {
            Log.Error($"Unable to find faction {faction}");
            return;
        }

        ent.Comp ??= EnsureComp<NpcFactionMemberComponent>(ent);
        if (!ent.Comp.Factions.Add(faction))
            return;

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    /// <summary>
    /// Adds this entity to the particular faction.
    /// </summary>
    public void AddFactions(Entity<NpcFactionMemberComponent?> ent, HashSet<ProtoId<NpcFactionPrototype>> factions, bool dirty = true)
    {
        ent.Comp ??= EnsureComp<NpcFactionMemberComponent>(ent);

        foreach (var faction in factions)
        {
            if (!_proto.HasIndex(faction))
            {
                Log.Error($"Unable to find faction {faction}");
                continue;
            }

            ent.Comp.Factions.Add(faction);
        }

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    /// <summary>
    /// Removes this entity from the particular faction.
    /// </summary>
    public void RemoveFaction(Entity<NpcFactionMemberComponent?> ent, string faction, bool dirty = true)
    {
        if (!_proto.HasIndex<NpcFactionPrototype>(faction))
        {
            Log.Error($"Unable to find faction {faction}");
            return;
        }

        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!ent.Comp.Factions.Remove(faction))
            return;

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    /// <summary>
    /// Remove this entity from all factions.
    /// </summary>
    public void ClearFactions(Entity<NpcFactionMemberComponent?> ent, bool dirty = true)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Factions.Clear();

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    public IEnumerable<EntityUid> GetNearbyHostiles(Entity<NpcFactionMemberComponent?, FactionExceptionComponent?> ent, float range)
    {
        if (!Resolve(ent, ref ent.Comp1, false))
            return Array.Empty<EntityUid>();

        var hostiles = GetNearbyFactions(ent, range, ent.Comp1.HostileFactions)
            // ignore mobs that have both hostile faction and the same faction,
            // otherwise having multiple factions is strictly negative
            .Where(target => !IsEntityFriendly((ent, ent.Comp1), target)
                && !IsSecurityExemptFromContrabandDetection((ent.Owner, ent.Comp1), target)
                && IsValidContrabandDetectionTarget((ent.Owner, ent.Comp1), target));

        if (ent.Comp1.Factions.Contains(ContrabandDetectionFaction))
        {
            hostiles = hostiles.Union(GetNearbyContrabandCarriers(ent.Owner, range)
                .Where(target => !IsEntityFriendly((ent, ent.Comp1), target)
                    && !IsSecurityExemptFromContrabandDetection((ent.Owner, ent.Comp1), target)
                    && IsValidContrabandDetectionTarget((ent.Owner, ent.Comp1), target)));
        }

        if (!Resolve(ent, ref ent.Comp2, false))
            return hostiles;

        // ignore anything from enemy faction that we are explicitly friendly towards
        var faction = (ent.Owner, ent.Comp2);
        return hostiles
            .Union(GetHostiles(faction))
            .Where(target => !IsIgnored(faction, target) && !IsSecurityExemptFromContrabandDetection((ent.Owner, ent.Comp1), target));
    }

    private IEnumerable<EntityUid> GetNearbyContrabandCarriers(EntityUid entity, float range)
    {
        var xform = Transform(entity);
        foreach (var ent in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(_xform.GetMapCoordinates((entity, xform)), range))
        {
            if (ent.Owner == entity)
                continue;

            if (!HasClass3Contraband(ent.Owner))
                continue;

            yield return ent.Owner;
        }
    }

    private bool HasClass3Contraband(EntityUid entity)
    {
        if (TryComp<ContrabandComponent>(entity, out var selfContraband) && IsClass3Contraband(selfContraband))
            return true;

        var scanned = new HashSet<EntityUid>();
        foreach (var item in _inventory.GetHandOrInventoryEntities(entity))
        {
            if (ContainsClass3ContrabandRecursive(item, scanned))
                return true;
        }

        return false;
    }

    private bool ContainsClass3ContrabandRecursive(EntityUid entity, HashSet<EntityUid> scanned)
    {
        if (!scanned.Add(entity))
            return false;

        if (TryComp<ContrabandComponent>(entity, out var contraband) && IsClass3Contraband(contraband))
            return true;

        if (!TryComp<ContainerManagerComponent>(entity, out var manager))
            return false;

        foreach (var container in manager.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (ContainsClass3ContrabandRecursive(contained, scanned))
                    return true;
            }
        }

        return false;
    }

    private bool IsValidContrabandDetectionTarget(Entity<NpcFactionMemberComponent?> source, EntityUid target)
    {
        if (!Resolve(source, ref source.Comp, false) || !source.Comp.Factions.Contains(ContrabandDetectionFaction))
            return true;

        return HasClass3Contraband(target);
    }

    private static bool IsClass3Contraband(ContrabandComponent contraband)
    {
        return contraband.Severity.ToString().StartsWith("Class3", StringComparison.Ordinal)
            || Class3EquivalentContrabandSeverities.Contains(contraband.Severity);
    }

    private bool IsSecurityExemptFromContrabandDetection(Entity<NpcFactionMemberComponent?> source, EntityUid target)
    {
        if (!Resolve(source, ref source.Comp, false) || !source.Comp.Factions.Contains(ContrabandDetectionFaction))
            return false;

        var accessLevels = _accessReader.FindAccessTags(target);
        foreach (var access in accessLevels)
        {
            if (ContrabandDetectionSecurityExemptions.Contains(access))
                return true;
        }

        return false;
    }

    public IEnumerable<EntityUid> GetNearbyFriendlies(Entity<NpcFactionMemberComponent?> ent, float range)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return Array.Empty<EntityUid>();

        return GetNearbyFactions(ent, range, ent.Comp.FriendlyFactions);
    }

    private IEnumerable<EntityUid> GetNearbyFactions(EntityUid entity, float range, HashSet<ProtoId<NpcFactionPrototype>> factions)
    {
        var xform = Transform(entity);
        foreach (var ent in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(_xform.GetMapCoordinates((entity, xform)), range))
        {
            if (ent.Owner == entity)
                continue;

            if (!factions.Overlaps(ent.Comp.Factions))
                continue;

            yield return ent.Owner;
        }
    }

    /// <remarks>
    /// 1-way and purely faction based, ignores faction exception.
    /// </remarks>
    public bool IsEntityFriendly(Entity<NpcFactionMemberComponent?> ent, Entity<NpcFactionMemberComponent?> other)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !Resolve(other, ref other.Comp, false))
            return false;

        return ent.Comp.Factions.Overlaps(other.Comp.Factions) || ent.Comp.FriendlyFactions.Overlaps(other.Comp.Factions);
    }

    public bool IsFactionFriendly(string target, string with)
    {
        return _factions[target].Friendly.Contains(with) && _factions[with].Friendly.Contains(target);
    }

    public bool IsFactionFriendly(string target, Entity<NpcFactionMemberComponent?> with)
    {
        if (!Resolve(with, ref with.Comp, false))
            return false;

        return with.Comp.Factions.All(x => IsFactionFriendly(target, x)) ||
               with.Comp.FriendlyFactions.Contains(target);
    }

    public bool IsFactionHostile(string target, string with)
    {
        return _factions[target].Hostile.Contains(with) && _factions[with].Hostile.Contains(target);
    }

    public bool IsFactionHostile(string target, Entity<NpcFactionMemberComponent?> with)
    {
        if (!Resolve(with, ref with.Comp, false))
            return false;

        return with.Comp.Factions.All(x => IsFactionHostile(target, x)) ||
               with.Comp.HostileFactions.Contains(target);
    }

    public bool IsFactionNeutral(string target, string with)
    {
        return !IsFactionFriendly(target, with) && !IsFactionHostile(target, with);
    }

    /// <summary>
    /// Makes the source faction friendly to the target faction, 1-way.
    /// </summary>
    public void MakeFriendly(string source, string target)
    {
        if (!_factions.TryGetValue(source, out var sourceFaction))
        {
            Log.Error($"Unable to find faction {source}");
            return;
        }

        if (!_factions.ContainsKey(target))
        {
            Log.Error($"Unable to find faction {target}");
            return;
        }

        sourceFaction.Friendly.Add(target);
        sourceFaction.Hostile.Remove(target);
        RefreshFactions();
    }

    /// <summary>
    /// Makes the source faction hostile to the target faction, 1-way.
    /// </summary>
    public void MakeHostile(string source, string target)
    {
        if (!_factions.TryGetValue(source, out var sourceFaction))
        {
            Log.Error($"Unable to find faction {source}");
            return;
        }

        if (!_factions.ContainsKey(target))
        {
            Log.Error($"Unable to find faction {target}");
            return;
        }

        sourceFaction.Friendly.Remove(target);
        sourceFaction.Hostile.Add(target);
        RefreshFactions();
    }

    private void RefreshFactions()
    {
        _factions = _proto.EnumeratePrototypes<NpcFactionPrototype>().ToFrozenDictionary(
            faction => faction.ID,
            faction =>  new FactionData
            {
                Friendly = faction.Friendly.ToHashSet(),
                Hostile = faction.Hostile.ToHashSet()
            });

        var query = AllEntityQuery<NpcFactionMemberComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.FriendlyFactions.Clear();
            comp.HostileFactions.Clear();
            RefreshFactions((uid, comp));
        }
    }
}
