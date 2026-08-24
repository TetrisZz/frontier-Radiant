using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.Humanoid;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Client.UserInterface.Controllers;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Goobstation.Languages.UI;

/// <summary>
/// Radiant Sector: opens the language selector from the button in the top gameplay panel.
/// </summary>
public sealed class LanguageMenuUIController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    private LanguageMenuWindow? _window;
    private LanguageMenuSystem? _languageMenu;
    private MenuButton? LanguageButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.LanguageButton;

    public void OnStateEntered(GameplayState state)
    {
        // Radiant Sector: EntitySystems are only guaranteed to exist after gameplay has started.
        _languageMenu = EntitySystemManager.GetEntitySystem<LanguageMenuSystem>();
        _languageMenu.OnLanguageMenuState += OpenOrUpdate;
    }

    public void OnStateExited(GameplayState state)
    {
        if (_languageMenu != null)
            _languageMenu.OnLanguageMenuState -= OpenOrUpdate;

        _languageMenu = null;
        _window?.Close();
        _window = null;
    }

    public void LoadButton()
    {
        if (LanguageButton != null)
            LanguageButton.OnPressed += OnLanguageButtonPressed;
    }

    public void UnloadButton()
    {
        if (LanguageButton != null)
            LanguageButton.OnPressed -= OnLanguageButtonPressed;
    }

    private void OnLanguageButtonPressed(ButtonEventArgs args)
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        // Radiant Sector: show the selector immediately from client-side character data.
        // The request afterwards replaces this provisional state with the authoritative server state.
        if (TryGetLocalNativeLanguage(out var nativeLanguage))
        {
            if (_languageMenu?.CachedState is { } cached && cached.NativeLanguage == nativeLanguage)
                OpenOrUpdate(cached.NativeLanguage, cached.NativeSelected, cached.CanUseNative, cached.CanUseGalactic);
            else
                OpenOrUpdate(nativeLanguage, false, true, true);
        }
        else if (_languageMenu?.CachedState is { } cached)
        {
            OpenOrUpdate(cached.NativeLanguage, cached.NativeSelected, cached.CanUseNative, cached.CanUseGalactic);
        }

        _languageMenu?.RequestMenu();
    }

    private void OpenOrUpdate(string nativeLanguage, bool nativeSelected, bool canUseNative, bool canUseGalactic)
    {
        if (_window == null)
        {
            _window = UIManager.CreateWindow<LanguageMenuWindow>();
            _window.OnClose += OnWindowClosed;
            _window.OpenCentered();
        }

        _window.UpdateState(nativeLanguage, nativeSelected, canUseNative, canUseGalactic);
        if (LanguageButton != null)
            LanguageButton.Pressed = true;
    }

    private void OnWindowClosed()
    {
        _window = null;
        if (LanguageButton != null)
            LanguageButton.Pressed = false;
    }

    // Radiant Sector: mirrored only for instant UI opening; server validation remains authoritative.
    private bool TryGetLocalNativeLanguage(out string language)
    {
        language = string.Empty;
        if (_playerManager.LocalEntity is not { Valid: true } player ||
            !_entityManager.TryGetComponent(player, out HumanoidAppearanceComponent? humanoid))
            return false;

        language = humanoid.Species.Id switch
        {
            "Reptilian" => "Синта'Унати",
            "Vox" => "Вокс-пиджин",
            "Diona" => "Корневой язык",
            "SlimePerson" => "Бабблилиш",
            "Moth" => "Моффик",
            "Arachnid" => "Щёлкающий",
            "Vulpkanin" => "Канилунц",
            "Tajaran" => "Сиик'тайр",
            "Resomi" => "Счечи",
            "Feroxi" => "Нехина",
            "Shadowkin" => "Сумеречный",
            "Dwarf" => "Кхаздар",
            "Oni" => "Кансэй",
            "Harpy" => "Аэрийский",
            "Goblin" => "Крикли",
            "Sheleg" => "Шелар",
            "DemonSpecies" => "Арканийский",
            "Felinid" => "НекоМетрический",
            _ => string.Empty,
        };

        return language.Length > 0;
    }
}
