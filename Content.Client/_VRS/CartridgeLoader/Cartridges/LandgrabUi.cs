using Content.Client.UserInterface.Fragments;
using Content.Shared._VRS.Planet;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._VRS.CartridgeLoader.Cartridges;

/// <summary>
/// Cartridge UI shell that owns a <see cref="LandgrabUiFragment"/> and forwards
/// its events back to the server as <see cref="CartridgeUiMessage"/>s.
/// </summary>
public sealed partial class LandgrabUi : UIFragment
{
    private LandgrabUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new LandgrabUiFragment();

        _fragment.OnPurchase += () =>
            userInterface.SendMessage(new CartridgeUiMessage(new LandgrabPurchaseMessage()));

        _fragment.OnSave += slot =>
            userInterface.SendMessage(new CartridgeUiMessage(new LandgrabSaveMessage { SlotName = slot }));

        _fragment.OnLoad += slot =>
            userInterface.SendMessage(new CartridgeUiMessage(new LandgrabLoadMessage { SlotName = slot }));

        _fragment.OnDelete += slot =>
            userInterface.SendMessage(new CartridgeUiMessage(new LandgrabDeleteSaveMessage { SlotName = slot }));

        _fragment.OnAbandon += () =>
            userInterface.SendMessage(new CartridgeUiMessage(new LandgrabAbandonMessage()));

        _fragment.OnEngraveDisk += label =>
            userInterface.SendMessage(new CartridgeUiMessage(new LandgrabWriteDiskMessage { Label = label }));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not LandgrabUiState landgrab)
            return;
        _fragment?.UpdateState(landgrab);
    }
}
