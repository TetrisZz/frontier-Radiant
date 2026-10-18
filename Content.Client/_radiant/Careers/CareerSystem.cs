using Content.Client.CharacterInfo;
using Content.Client.Lobby;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared._radiant.Careers;
using Content.Shared.Preferences;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Careers;

public sealed class CareerSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IClientPreferencesManager _prefs = default!;
    [Dependency] private readonly JobRequirementsManager _requirements = default!;

    private CareerWindow? _window;
    public event Action<CareerStateEvent>? StateUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CareerStateEvent>(OnState);
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnCharacterControls);
    }

    public override void Shutdown()
    {
        CloseWindow();
        base.Shutdown();
    }

    public Button CreateButton()
    {
        var button = new Button { Text = Loc.GetString("careers-client-button") };
        button.OnPressed += _ => OpenWindow();
        return button;
    }

    public void OpenWindow()
    {
        if (_window == null)
        {
            var window = new CareerWindow();
            _window = window;
            window.SelectRequested += OnSelect;
            window.OnClose += () =>
            {
                window.SelectRequested -= OnSelect;
                _window = null;
                window.Dispose();
            };
        }

        _window.SetLoading();
        _window.OpenCentered();
        RaiseNetworkEvent(new CareerRequestEvent());
    }

    public void CloseWindow() => _window?.Close();

    public void SelectJobTitle(Guid characterId, string title)
    {
        RaiseNetworkEvent(new CareerSelectJobTitleEvent(characterId, title));
    }

    private void OnSelect(string profession, bool confirmReset)
    {
        RaiseNetworkEvent(new CareerSelectEvent(profession, confirmReset));
    }

    private void OnState(CareerStateEvent ev, EntitySessionEventArgs args)
    {
        if (_prefs.Preferences?.SelectedCharacter is HumanoidCharacterProfile profile
            && profile.Career.CharacterId == ev.Career.CharacterId)
        {
            profile.Career = ev.Career.Clone();
            _requirements.RefreshCareerRequirements();
            StateUpdated?.Invoke(ev);
        }
        _window?.UpdateState(ev);
    }

    private void OnCharacterControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent ev)
    {
        if (ev.Entity == _players.LocalEntity)
            ev.Controls.Add(CreateButton());
    }
}
