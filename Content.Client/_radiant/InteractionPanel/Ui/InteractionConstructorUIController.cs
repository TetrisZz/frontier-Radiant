using Content.Client.Interaction.Panel.Ui;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client.UserInterface.Systems.Interaction;

public sealed class InteractionConstructorUIController : UIController
{
    private InteractionConstructorMenu? _window;

    public void ToggleWindow()
    {
        if (_window == null)
        {
            _window = UIManager.CreateWindow<InteractionConstructorMenu>();
            _window.OnClose += OnWindowClosed;

            _window.OpenCenteredLeft();
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