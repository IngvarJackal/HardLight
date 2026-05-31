using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Systems;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Shared._Goobstation.Research;

public static class SharedResearchSystemExtensions
{
    public static int GetTierCompletionPercentage(this SharedResearchSystem system,
        TechnologyDatabaseComponent component,
        TechDisciplinePrototype techDiscipline,
        IPrototypeManager prototypeManager)
    {
        var allTech = prototypeManager.EnumeratePrototypes<TechnologyPrototype>()
            .Where(p => system.CanContributeToDisciplineProgress(p, techDiscipline.ID)).ToList();

        if (allTech.Count == 0)
            return 0;

        var percentage = (float) component.UnlockedTechnologies
            .Select(prototypeManager.Index<TechnologyPrototype>)
            .Where(x => system.CanContributeToDisciplineProgress(x, techDiscipline.ID))
            .Count() / (float) allTech.Count * 100f;

        return (int) Math.Clamp(percentage, 0, 100);
    }
}
