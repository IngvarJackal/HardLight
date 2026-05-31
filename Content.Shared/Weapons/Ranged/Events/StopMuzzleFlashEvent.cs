using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Events;

[Serializable, NetSerializable]
public sealed class StopMuzzleFlashEvent : EntityEventArgs
{
    public NetEntity Uid;

    public StopMuzzleFlashEvent(NetEntity uid)
    {
        Uid = uid;
    }
}
