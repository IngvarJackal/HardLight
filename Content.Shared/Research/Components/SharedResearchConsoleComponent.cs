using Content.Shared._Goobstation.Research;
using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components
{
    [NetSerializable, Serializable]
    public enum ResearchConsoleUiKey : byte
    {
        Key,
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleUnlockTechnologyMessage : BoundUserInterfaceMessage
    {
        public string Id;

        public ConsoleUnlockTechnologyMessage(string id)
        {
            Id = id;
        }
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleServerSelectionMessage : BoundUserInterfaceMessage
    {

    }

    [Serializable, NetSerializable]
    public sealed class ResearchConsoleBoundInterfaceState : BoundUserInterfaceState
    {
        public int Points;

        /// <summary>
        /// Goobstation R&amp;D console rework: all researches and their availabilities, used by the Fancy console UI.
        /// </summary>
        public Dictionary<string, ResearchAvailability> Researches;

        public ResearchConsoleBoundInterfaceState(int points, Dictionary<string, ResearchAvailability> researches)
        {
            Points = points;
            Researches = researches;
        }
    }
}
