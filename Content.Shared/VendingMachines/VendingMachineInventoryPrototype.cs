using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared.VendingMachines
{
    [Prototype]
    public sealed partial class VendingMachineInventoryPrototype : IPrototype
    {
        [ViewVariables]
        [IdDataField]
        public string ID { get; private set; } = default!;

        [DataField("startingInventory", customTypeSerializer:typeof(PrototypeIdDictionarySerializer<uint, EntityPrototype>))]
        public Dictionary<string, uint> StartingInventory { get; private set; } = new();

        /// <summary>
        /// Optional parent pack to inherit inventory entries from.
        /// Child entries override matching parent keys.
        /// </summary>
        [DataField("inherits", customTypeSerializer: typeof(PrototypeIdSerializer<VendingMachineInventoryPrototype>))]
        public string? Inherits;

        /// <summary>
        /// If true, inherited inventory entries are expanded to uint.MaxValue before local overrides.
        /// Useful for POI/debug infinite-stock variants without repeating all keys.
        /// </summary>
        [DataField("inheritAsUnlimited")]
        public bool InheritAsUnlimited;

        [DataField("emaggedInventory", customTypeSerializer:typeof(PrototypeIdDictionarySerializer<uint, EntityPrototype>))]
        public Dictionary<string, uint>? EmaggedInventory { get; private set; }

        [DataField("contrabandInventory", customTypeSerializer:typeof(PrototypeIdDictionarySerializer<uint, EntityPrototype>))]
        public Dictionary<string, uint>? ContrabandInventory { get; private set; }
    }
}
