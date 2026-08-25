using Content.Shared._radiant.Languages;

namespace Content.Client._radiant.Languages.UI;

public sealed class LanguageTranslatorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private LanguageTranslatorWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new LanguageTranslatorWindow();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is LanguageTranslatorUiState translatorState)
            _window?.Populate(translatorState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
        _window = null;
    }
}
