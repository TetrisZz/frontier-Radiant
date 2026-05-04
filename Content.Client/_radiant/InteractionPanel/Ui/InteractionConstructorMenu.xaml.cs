using System.Text.RegularExpressions;
using Content.Client.UserInterface.Systems.Interaction;
using Content.Shared.Chat.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.Interaction.Panel.Ui
{
    public sealed partial class InteractionConstructorMenu : DefaultWindow
    {
        private readonly InteractionUIController _interactionPanelController;

        public BoxContainer PreviewContainer => this.FindControl<BoxContainer>("PreviewContainer");
        public LineEdit NameLine => this.FindControl<LineEdit>("NameLine");
        public LineEdit MessageLine => this.FindControl<LineEdit>("MessageLine");
        public Button AddButton => this.FindControl<Button>("AddButton");

        private ErrorLevel _errorLevel = ErrorLevel.None;

        public InteractionConstructorMenu()
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            _interactionPanelController = UserInterfaceManager.GetUIController<InteractionUIController>();

            NameLine.OnTextChanged += OnNameLineChanged;
            MessageLine.OnTextChanged += OnMessageLineChanged;
            NameLine.OnTextChanged += _ => UpdatePreview();

            _errorLevel |= ErrorLevel.NameLine;
            _errorLevel |= ErrorLevel.MessageLine;
            NameLine.ModulateSelfOverride = Color.Red;
            MessageLine.ModulateSelfOverride = Color.Red;

            AddButton.OnPressed += OnAddButtonPressed;
            UpdatePreview();
            UpdateButtonState();
        }

        private void UpdatePreview()
        {
            PreviewContainer.RemoveAllChildren();

            var buttonText = string.IsNullOrWhiteSpace(NameLine.Text)
                ? Loc.GetString("interaction-constructor-unnamed")
                : NameLine.Text;

            PreviewContainer.AddChild(new Button
            {
                Text = buttonText,
                MinWidth = 420,
                MinHeight = 32
            });
        }

        private void OnNameLineChanged(LineEdit.LineEditEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Text))
            {
                _errorLevel |= ErrorLevel.NameLine;
                NameLine.ModulateSelfOverride = Color.Red;
            }
            else
            {
                _errorLevel &= ~ErrorLevel.NameLine;
                NameLine.ModulateSelfOverride = null;
            }

            UpdateButtonState();
        }

        private void OnMessageLineChanged(LineEdit.LineEditEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Text))
            {
                _errorLevel |= ErrorLevel.MessageLine;
                MessageLine.ModulateSelfOverride = Color.Red;
            }
            else
            {
                _errorLevel &= ~ErrorLevel.MessageLine;
                MessageLine.ModulateSelfOverride = null;
            }

            UpdateButtonState();
        }

        private void UpdateButtonState()
        {
            AddButton.Disabled = _errorLevel != ErrorLevel.None;
        }

        private void OnAddButtonPressed(BaseButton.ButtonEventArgs args)
        {
            if (_errorLevel != ErrorLevel.None)
                return;

            var interactionPrototype = PrepareInteractionPrototype();
            _interactionPanelController.AddConstructor(interactionPrototype);
        }

        private InteractionPrototype PrepareInteractionPrototype()
        {
            var displayName = NameLine.Text.Trim();
            var actionText = MessageLine.Text.Trim();

            return new InteractionPrototype
            {
                ID = BuildInteractionId(displayName),
                Name = displayName,
                // Keep constructor simple: one action phrase generates all channels.
                UserMessages = new List<string> { $"{actionText} $target" },
                TargetMessages = new List<string> { "$user " + actionText },
                OtherMessages = new List<string> { "$user " + actionText + " $target" },
                Icon = "/Textures/_radiant/Interface/InteractionPanel/heart.png",
                Category = "body",
                AllowedGenders = new List<string> { "all" },
                AllowedSpecies = new List<string> { "all" },
                NearestAllowedGenders = new List<string> { "all" },
                NearestAllowedSpecies = new List<string> { "all" }
            };
        }

        private static string BuildInteractionId(string name)
        {
            var sanitized = Regex.Replace(name, "[^a-zA-Z0-9]+", string.Empty);
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "Custom";

            return $"InteractionCustom{sanitized}{Guid.NewGuid():N}";
        }

        [Flags]
        private enum ErrorLevel : byte
        {
            None = 0,
            NameLine = 1 << 0,
            MessageLine = 1 << 1,
        }
    }
}
