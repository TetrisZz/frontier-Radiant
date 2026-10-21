using System.Linq;
using System.Numerics;
using Content.Client.Audio;
using Content.Client.Options.UI;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Radio;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client.Corvax.TTS;

public sealed class TTSSettingsWindow : DefaultWindow
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public TTSSettingsWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("tts-settings-title");
        MinSize = new Vector2(500, 420);
        SetSize = new Vector2(560, 640);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        Contents.AddChild(body);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        body.AddChild(scroll);
        var settings = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        scroll.AddChild(settings);
        var controller = new OptionsTabControlRow();
        body.AddChild(controller);

        AddVolume(CCCVars.TTSVolume, "tts-settings-volume");
        AddVolume(CCCVars.TTSRadioVolume, "tts-settings-radio-volume");
        AddVolume(CCCVars.TTSAnnouncementVolume, "tts-settings-announcement-volume");

        settings.AddChild(new Label { Text = Loc.GetString("tts-settings-automatic"), StyleClasses = { "LabelHeading" } });
        var automatic = new CheckBox { Text = Loc.GetString("tts-settings-automatic-enabled") };
        settings.AddChild(automatic);
        controller.AddOptionCheckBox(CCCVars.TTSAutomaticEnabled, automatic);
        AddVolume(CCCVars.TTSAutomaticVolume, "tts-settings-automatic-volume");

        settings.AddChild(new Label { Text = Loc.GetString("tts-settings-channels"), StyleClasses = { "LabelHeading" } });
        var commands = new BoxContainer { SeparationOverride = 4 };
        settings.AddChild(commands);
        var all = new Button { Text = Loc.GetString("tts-settings-all"), HorizontalExpand = true };
        var none = new Button { Text = Loc.GetString("tts-settings-none"), HorizontalExpand = true };
        commands.AddChild(all);
        commands.AddChild(none);

        var channels = new Dictionary<string, CheckBox>();
        foreach (var channel in _prototypes.EnumeratePrototypes<RadioChannelPrototype>().OrderBy(p => p.LocalizedName))
        {
            var checkbox = new CheckBox { Text = channel.LocalizedName, Modulate = channel.Color };
            channels.Add(channel.ID, checkbox);
            settings.AddChild(checkbox);
        }
        var channelOption = controller.AddOption(new ChannelOption(controller, _cfg, channels));
        all.OnPressed += _ => channelOption.SetAll(true);
        none.OnPressed += _ => channelOption.SetAll(false);
        controller.Initialize();

        void AddVolume(CVarDef<float> cvar, string label)
        {
            var slider = new OptionSlider { Title = Loc.GetString(label) };
            settings.AddChild(slider);
            controller.AddOptionPercentSlider(cvar, slider, scale: ContentAudioSystem.TtsMultiplier);
        }
    }

    private sealed class ChannelOption : BaseOptionCVar<string>
    {
        private readonly Dictionary<string, CheckBox> _channels;
        private readonly HashSet<string> _unknown = new(StringComparer.Ordinal);

        public ChannelOption(OptionsTabControlRow controller, IConfigurationManager cfg,
            Dictionary<string, CheckBox> channels) : base(controller, cfg, CCCVars.TTSMutedRadioChannels)
        {
            _channels = channels;
            foreach (var checkbox in channels.Values)
                checkbox.OnToggled += _ => ValueChanged();
        }

        protected override string Value
        {
            get => string.Join(',', _unknown.Concat(_channels.Where(c => !c.Value.Pressed).Select(c => c.Key))
                .OrderBy(c => c, StringComparer.Ordinal));
            set
            {
                var muted = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.Ordinal);
                _unknown.Clear();
                _unknown.UnionWith(muted.Where(c => !_channels.ContainsKey(c)));
                foreach (var (id, checkbox) in _channels)
                    checkbox.Pressed = !muted.Contains(id);
            }
        }

        protected override bool IsValueEqual(string a, string b) =>
            a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
                .SetEquals(b.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public void SetAll(bool enabled)
        {
            foreach (var checkbox in _channels.Values)
                checkbox.Pressed = enabled;
            ValueChanged();
        }
    }
}
