using Content.Shared._Starlight.Medical.Surgery;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Starlight.Medical.Surgery.BodyScanner;

// Radiant sector start - dedicated body scanner BUI, independent of the handheld analyzer.
[UsedImplicitly]
public sealed class BodyScannerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private BodyScannerWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<BodyScannerWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

        if (State is BodyScannerBoundUserInterfaceState state)
            _window.Populate(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is BodyScannerBoundUserInterfaceState scannerState)
            _window?.Populate(scannerState);
    }
}
// Radiant sector end
