using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._radiant.Skills;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Skills;

public sealed class SkillSelectionSystem : EntitySystem
{
    [Dependency] private readonly IClientPreferencesManager _preferences = default!;
    [Dependency] private readonly IStateManager _states = default!;
    private FancyWindow? _warning;

    public override void Initialize()
    {
        SubscribeNetworkEvent<SkillSelectionRequiredEvent>(_ => ShowWarning());
        SubscribeNetworkEvent<TickerJoinGameEvent>(_ => _warning?.Close());
    }

    public bool CheckSelection()
    {
        if (_preferences.Preferences?.SelectedCharacter is HumanoidCharacterProfile profile &&
            ProfessionalSkillRules.CanJoin(profile.SkillLevels))
            return true;

        ShowWarning();
        return false;
    }

    private void ShowWarning()
    {
        if (_states.CurrentState is not LobbyState)
            return;
        if (_warning is { IsOpen: true })
            return;

        _warning = new FancyWindow
        {
            Title = Loc.GetString("professional-skills-selection-title"),
            MinWidth = 390,
            MaxWidth = 390,
            Resizable = false,
        };
        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 12,
            Margin = new Thickness(12),
        };
        var message = new RichTextLabel();
        message.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("professional-skills-selection-required",
            ("minimum", ProfessionalSkillRules.MinimumSpent))));
        body.AddChild(message);
        var setup = new Button { Text = Loc.GetString("professional-skills-selection-open"), HorizontalExpand = true };
        setup.OnPressed += _ =>
        {
            _warning?.Close();
            if (_states.CurrentState is LobbyState lobby)
                lobby.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
        };
        body.AddChild(setup);
        _warning.ContentsContainer.AddChild(body);
        var window = _warning;
        window.OnClose += () =>
        {
            if (_warning == window)
                _warning = null;
            window.Dispose();
        };
        window.OpenCentered();
    }

    public override void Shutdown()
    {
        _warning?.Close();
        base.Shutdown();
    }
}
