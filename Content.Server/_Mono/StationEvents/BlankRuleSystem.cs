using Content.Server.StationEvents.Events;
using JetBrains.Annotations;

namespace Content.Server._Mono.StationEvents;

// VRS: Ported from Triad_Sector — blank station event rule used for announcement-only events.
[UsedImplicitly]
public sealed class FalseAlarmRule : StationEventSystem<BlankRuleComponent>
{
}
