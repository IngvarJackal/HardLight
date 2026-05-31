using Content.Server.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Maths;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;

namespace Content.Server.DeviceLinking.Systems;

public sealed partial class GunSignalControlSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _signalSystem = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    private const float TriggerShotDistance = 100f;

    public override void Initialize()
    {
        SubscribeLocalEvent<GunSignalControlComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<GunSignalControlComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void OnInit(Entity<GunSignalControlComponent> gunControl, ref MapInitEvent args)
    {
        _signalSystem.EnsureSinkPorts(gunControl, gunControl.Comp.TriggerPort, gunControl.Comp.TogglePort, gunControl.Comp.OnPort, gunControl.Comp.OffPort);
    }

    private void OnSignalReceived(Entity<GunSignalControlComponent> gunControl, ref SignalReceivedEvent args)
    {
        if (!TryComp<GunComponent>(gunControl, out var gun))
            return;

        if (args.Port == gunControl.Comp.TriggerPort)
        {
            // VRS: signal-triggered firing should use the same default forward direction as manual
            // aiming. AttemptShoot(gunUid, gun) builds a local target at gun.DefaultDirection but can
            // be interpreted with an uncorrected rotation for mounted ship guns, causing a consistent
            // 90-degree offset on trigger fire (HL #1730).
            var xform = Transform(gunControl);
            var localDirection = xform.LocalRotation.ToVec();

            if (gun.DefaultDirection.LengthSquared() > float.Epsilon)
                localDirection = xform.LocalRotation.RotateVec(gun.DefaultDirection.Normalized());

            var localTargetPos = xform.LocalPosition + localDirection * TriggerShotDistance;
            var targetCoordinates = new EntityCoordinates(xform.ParentUid, localTargetPos);
            _gun.AttemptShoot(gunControl, gunControl, gun, targetCoordinates);
        }

        if (!TryComp<AutoShootGunComponent>(gunControl, out var autoShoot))
            return;

        if (args.Port == gunControl.Comp.TogglePort)
           _gun.SetEnabled(gunControl, autoShoot, !autoShoot.Enabled);

        if (args.Port == gunControl.Comp.OnPort)
            _gun.SetEnabled(gunControl, autoShoot, true);

        if (args.Port == gunControl.Comp.OffPort)
            _gun.SetEnabled(gunControl, autoShoot, false);
    }
}
