using Content.Shared._Goobstation.Languages;

namespace Content.Client._Goobstation.Languages;

/// <summary>
/// Radiant Sector: client network bridge for the language selection window.
/// </summary>
public sealed class LanguageMenuSystem : EntitySystem
{
    public event Action<string, bool, bool, bool>? OnLanguageMenuState;

    // Radiant Sector: lets the UI open immediately with the last authoritative state,
    // while a fresh request is still travelling to the server.
    public (string NativeLanguage, bool NativeSelected, bool CanUseNative, bool CanUseGalactic)? CachedState { get; private set; }

    public override void Initialize()
    {
        SubscribeNetworkEvent<LanguageMenuStateEvent>(OnLanguageMenuStateReceived);
    }

    public void RequestMenu()
    {
        RaiseNetworkEvent(new LanguageMenuRequestEvent());
    }

    public void SelectLanguage(bool native)
    {
        RaiseNetworkEvent(new LanguageMenuSelectEvent(native));
    }

    private void OnLanguageMenuStateReceived(LanguageMenuStateEvent ev)
    {
        CachedState = (ev.NativeLanguage, ev.NativeSelected, ev.CanUseNative, ev.CanUseGalactic);
        OnLanguageMenuState?.Invoke(ev.NativeLanguage, ev.NativeSelected, ev.CanUseNative, ev.CanUseGalactic);
    }
}
