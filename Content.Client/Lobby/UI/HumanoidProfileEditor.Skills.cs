using System.Linq;
using Content.Shared._Goobstation.Languages;
using Content.Shared._radiant.Skills;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private BoxContainer? _skillsList;
    private BoxContainer? _skillDetails;
    private BoxContainer? _skillLanguages;
    private Label? _skillPoints;
    private ProfessionalSkill _selectedSkill;

    private static Color SkillColor(ProfessionalSkill skill) => Color.FromHex(skill switch
    {
        ProfessionalSkill.Piloting => "#AA91F2",
        ProfessionalSkill.Shooting => "#F08080",
        ProfessionalSkill.Medicine => "#66D5E8",
        ProfessionalSkill.Engineering => "#F3CB70",
        ProfessionalSkill.Salvage => "#DBAA75",
        ProfessionalSkill.Botany => "#85D989",
        ProfessionalSkill.Cooking => "#E6B78E",
        _ => "#CDA7ED",
    });

    private static BoxContainer SkillColumn() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 8,
        HorizontalExpand = true,
    };

    private static PanelContainer SkillPanel(Control content)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#18232e"),
                BorderColor = Color.FromHex("#344c60"),
                BorderThickness = new Thickness(1),
            },
        };
        content.Margin = new Thickness(10);
        panel.AddChild(content);
        return panel;
    }

    private static RichTextLabel SkillText(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(text);
        return label;
    }

    private void SetupSkills()
    {
        var root = SkillColumn();
        root.Margin = new Thickness(12);
        root.HorizontalAlignment = Control.HAlignment.Stretch;
        root.VerticalExpand = true;
        _skillPoints = new Label();
        root.AddChild(_skillPoints);

        var columns = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        root.AddChild(columns);
        var main = SkillColumn();
        var mainScroll = new ScrollContainer
        {
            HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false,
            SizeFlagsStretchRatio = 0.64f,
        };
        mainScroll.OnResized += () => main.MaxWidth = Math.Max(1, mainScroll.Size.X - 16);
        mainScroll.AddChild(main);
        columns.AddChild(mainScroll);
        main.AddChild(new Label { Text = Loc.GetString("professional-skills-heading") });
        _skillsList = SkillColumn();
        main.AddChild(_skillsList);
        _skillDetails = SkillColumn();
        main.AddChild(SkillPanel(_skillDetails));

        var languages = SkillColumn();
        languages.HorizontalExpand = false;
        languages.HorizontalAlignment = Control.HAlignment.Right;
        languages.MinWidth = 250;
        languages.AddChild(new Label { Text = Loc.GetString("professional-skills-languages") });
        languages.ToolTip = Loc.GetString("professional-skills-languages-help");
        _skillLanguages = SkillColumn();
        _skillLanguages.SeparationOverride = 0;
        languages.AddChild(_skillLanguages);
        var languageViewport = new ScrollContainer
        {
            HorizontalExpand = true, VerticalExpand = true,
            HScrollEnabled = false, VScrollEnabled = false, ReturnMeasure = false,
            SizeFlagsStretchRatio = 0.36f,
        };
        languageViewport.AddChild(languages);
        languageViewport.OnResized += () => languages.MaxWidth = Math.Max(1, languageViewport.Size.X);
        columns.AddChild(languageViewport);
        // Do not propagate the unwrapped help text's desired width into the
        // profile editor's horizontal box: it would squeeze out the sprite view.
        // Measure/arrange the content using the space left beside that preview.
        var viewport = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = false,
            ReturnMeasure = false,
        };
        viewport.AddChild(root);
        // The editor can measure this tab before its final width is known.
        // Re-measure wrapped text against the actual viewport, not that initial
        // desired width, including after resizing the window or changing UI scale.
        viewport.OnResized += () => root.MaxWidth = Math.Max(1, viewport.Size.X - 24);
        TabContainer.AddChild(viewport);
        TabContainer.SetTabTitle(TabContainer.ChildCount - 1, Loc.GetString("professional-skills-tab"));
        RefreshSkills();
    }

    private void RefreshSkills()
    {
        if (_skillsList == null || _skillPoints == null)
            return;
        _skillPoints.Text = Loc.GetString("professional-skills-points",
            ("remaining", ProfessionalSkillRules.Budget - (Profile?.SkillPointsSpent ?? 0)),
            ("total", ProfessionalSkillRules.Budget));
        _skillsList.DisposeAllChildren();
        foreach (var skill in Enum.GetValues<ProfessionalSkill>())
        {
            var level = Profile?.SkillLevels[(int) skill] ?? 0;
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            var name = new Button
            {
                Text = Loc.GetString(ProfessionalSkillRules.NameKey(skill)),
                HorizontalExpand = true,
                ToggleMode = true,
                Pressed = _selectedSkill == skill,
                Modulate = SkillColor(skill),
            };
            name.OnPressed += _ => { _selectedSkill = skill; RefreshSkills(); };
            row.AddChild(name);
            for (var rank = 0; rank <= ProfessionalSkillRules.Maximum(skill); rank++)
            {
                var selectedRank = rank;
                var button = new Button
                {
                    Text = rank.ToString(), MinWidth = 32, MaxWidth = 32, ToggleMode = true, Pressed = rank == level,
                    Modulate = rank == level ? SkillColor(skill) : Color.White,
                    ToolTip = Loc.GetString($"{ProfessionalSkillRules.NameKey(skill)}-level-{rank}"),
                    Disabled = Profile == null || Profile.SkillPointsSpent - level + rank > ProfessionalSkillRules.Budget,
                };
                button.OnPressed += _ => SetSkill(skill, selectedRank);
                row.AddChild(button);
            }
            // Reserve the same five level columns even for skills with fewer ranks.
            for (var rank = ProfessionalSkillRules.Maximum(skill) + 1; rank <= 4; rank++)
                row.AddChild(new Control { MinWidth = 32, MaxWidth = 32 });
            _skillsList.AddChild(row);
        }
        RefreshSkillDetails();
        RefreshSkillLanguages();
    }

    private void RefreshSkillDetails()
    {
        if (_skillDetails == null)
            return;
        _skillDetails.DisposeAllChildren();
        var level = Profile?.SkillLevels[(int) _selectedSkill] ?? 0;
        _skillDetails.AddChild(new Label
        {
            Text = Loc.GetString("professional-skills-current-level",
                ("skill", Loc.GetString(ProfessionalSkillRules.NameKey(_selectedSkill))), ("level", level)),
            Modulate = SkillColor(_selectedSkill),
        });
        _skillDetails.AddChild(SkillText(Loc.GetString($"professional-skills-experience-{level}")));
        var text = SkillText(Loc.GetString($"{ProfessionalSkillRules.NameKey(_selectedSkill)}-level-{level}"));
        text.Modulate = Color.FromHex("#d6e6f0");
        _skillDetails.AddChild(text);
    }

    private void RefreshSkillLanguages()
    {
        if (_skillLanguages == null)
            return;
        _skillLanguages.DisposeAllChildren();
        var native = Profile == null ? null : SpeciesLanguageUtility.GetNativeLanguage(Profile.Species.Id);
        // Use the same species lookup as speech, not a second race-to-language table.
        var names = _prototypeManager.EnumeratePrototypes<SpeciesPrototype>()
            .Select(species => SpeciesLanguageUtility.GetNativeLanguage(species.ID))
            .Where(language => language != null)
            .Cast<string>()
            .Append("Двоичный")
            .Distinct()
            // System.StringComparer is not exposed to sandboxed content.
            .OrderBy(language => language)
            .Prepend("Общегалактический")
            .ToArray();
        var colors = new[] { "#80CFFF", "#D888E8", "#8ED5E8", "#24D1B9", "#A489A0", "#7FD8FF",
            "#D69B3D", "#D95B67", "#C78E42", "#BC8A60", "#A7C746", "#C7DF2E", "#E5A1C7",
            "#5FAEE3", "#F1D37A", "#73CE73", "#D6A36E", "#C29EFF", "#4DBFD9", "#B5B9DF", "#B0B0B0" };
        for (var i = 0; i < names.Length; i++)
        {
            var language = names[i];
            var known = Profile != null && (language == "Общегалактический"
                ? !Profile.TraitPreferences.Contains("NativeLanguageOnly")
                : language == native && !Profile.TraitPreferences.Contains("NativeLanguageUnfamiliar"));
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
            row.AddChild(new Label { Text = known ? "✓" : "—", MinWidth = 20 });
            var text = SkillText(language);
            text.Modulate = Color.FromHex(colors[i % colors.Length]);
            row.AddChild(text);
            row.ToolTip = Loc.GetString(known ? "professional-skills-language-known" : "professional-skills-language-unknown");
            _skillLanguages.AddChild(row);
        }
    }

    private void SetSkill(ProfessionalSkill skill, int level)
    {
        if (Profile == null)
            return;
        _selectedSkill = skill;
        Profile = Profile.WithSkillLevel(skill, level);
        SetDirty();
        RefreshSkills();
    }
}
