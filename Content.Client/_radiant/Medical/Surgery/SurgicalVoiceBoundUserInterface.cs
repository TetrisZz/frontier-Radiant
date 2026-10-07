using System.Linq;
using Content.Client.Corvax.TTS;
using Content.Shared._radiant.Medical.Surgery;
using Content.Shared.Corvax.TTS;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._radiant.Medical.Surgery;

public sealed class SurgicalVoiceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SurgicalVoiceWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<SurgicalVoiceWindow>();
        _window.OnSave += voice => SendMessage(new SurgicalVoiceMessage(voice));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SurgicalVoiceState voices)
            _window?.SetState(voices);
    }
}

public sealed class SurgicalVoiceWindow : DefaultWindow
{
    private readonly OptionButton _voices = new() { HorizontalExpand = true };
    private readonly Label _originalVoice = new();
    private readonly Button _save = new() { Text = Loc.GetString("surgical-voice-save") };
    private readonly Button _preview = new() { Text = Loc.GetString("surgical-voice-preview") };
    private List<TTSVoicePrototype> _options = new();
    private int _selected;
    public event Action<string>? OnSave;

    public SurgicalVoiceWindow()
    {
        Title = Loc.GetString("surgical-voice-title");
        MinWidth = 420;
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
        layout.AddChild(new Label { Text = Loc.GetString("surgical-voice-hint") });
        layout.AddChild(_voices);
        layout.AddChild(_originalVoice);
        layout.AddChild(_preview);
        layout.AddChild(_save);
        Contents.AddChild(layout);
        _voices.OnItemSelected += args => { _selected = args.Id; _voices.SelectId(args.Id); };
        _save.OnPressed += _ => { if (_options.Count > 0) OnSave?.Invoke(_options[_selected].ID); };
        _preview.OnPressed += _ =>
        {
            if (_options.Count > 0)
                IoCManager.Resolve<IEntityManager>().System<TTSSystem>().RequestPreviewTTS(_options[_selected].ID);
        };
    }

    public void SetState(SurgicalVoiceState state)
    {
        _originalVoice.Visible = state.OriginalVoice != null;
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        _originalVoice.Text = state.OriginalVoice is { } original
            && prototypes.TryIndex<TTSVoicePrototype>(original, out var originalPrototype)
            ? Loc.GetString("restoration-original-voice", ("voice", Loc.GetString(originalPrototype.Name)))
            : "";
        _options = IoCManager.Resolve<IPrototypeManager>().EnumeratePrototypes<TTSVoicePrototype>()
            .Where(v => v.RoundStart && HumanoidCharacterProfile.CanHaveVoice(v, state.Sex))
            .OrderBy(v => Loc.GetString(v.Name)).ToList();
        _voices.Clear();
        for (var i = 0; i < _options.Count; i++)
            _voices.AddItem(Loc.GetString(_options[i].Name)
                + (_options[i].ID == state.OriginalVoice ? Loc.GetString("restoration-original-marker") : ""), i);
        _selected = Math.Max(0, _options.FindIndex(v => v.ID == state.CurrentVoice));
        _save.Disabled = _preview.Disabled = _options.Count == 0;
        if (_options.Count > 0) _voices.SelectId(_selected);
    }
}
