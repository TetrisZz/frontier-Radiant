using Content.Client.Interaction.Panel.Ui;
using Content.Shared.Chat.Prototypes;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client.UserInterface.Systems.Interaction;

public sealed class InteractionEditorUIController : UIController
{
    private InteractionEditorMenu? _window;

    public void ToggleWindow(InteractionPrototype prototype)
    {
        if (_window == null)
        {
            _window = UIManager.CreateWindow<InteractionEditorMenu>();
            _window.OnClose += OnWindowClosed;

            _window.OpenCenteredLeft();

            _window.SetData(prototype);
        }
        else
        {
            _window.Close();
        }
    }

    private void OnWindowClosed()
    {
        _window = null;
    }
}