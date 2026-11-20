using Content.Shared._radiant.Skills;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Skills;

public sealed class CharacterSkillsControl : BoxContainer
{
    public CharacterSkillsControl(int[] levels)
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;
        Margin = new Thickness(0, 12, 0, 0);
        HorizontalExpand = true;

        var heading = new Label { Text = Loc.GetString("professional-skills-character-heading") };
        heading.StyleClasses.Add("LabelHeading");
        AddChild(heading);
        var hint = Text(Loc.GetString("professional-skills-character-expand"));
        hint.Modulate = Color.FromHex("#98AABB");
        AddChild(hint);

        var selected = 0;
        for (var i = 0; i < Math.Min(levels.Length, ProfessionalSkillRules.Count); i++)
        {
            var skill = (ProfessionalSkill) i;
            var level = Math.Clamp(levels[i], 0, ProfessionalSkillRules.Maximum(skill));
            if (level == 0)
                continue;

            AddChild(CreateCard(skill, level));
            selected++;
        }

        var footer = Text(Loc.GetString(selected > 0
            ? "professional-skills-character-inherited"
            : "professional-skills-character-empty"));
        footer.Modulate = Color.FromHex("#98AABB");
        AddChild(footer);
    }

    private static PanelContainer CreateCard(ProfessionalSkill skill, int level)
    {
        var color = SkillPresentation.SkillColor(skill);
        var header = new CollapsibleHeading(Loc.GetString(ProfessionalSkillRules.NameKey(skill)))
        {
            MinHeight = 42,
            ChevronMargin = new Thickness(10, 0, 8, 0),
            StyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#21313E"),
                BorderColor = color,
                BorderThickness = new Thickness(3, 0, 0, 0),
            },
        };
        header.Label.HorizontalExpand = true;
        header.Label.FontColorOverride = color;
        header.Label.Margin = new Thickness(0, 0, 8, 0);

        var badge = new PanelContainer
        {
            Margin = new Thickness(0, 7, 10, 7),
            VerticalAlignment = VAlignment.Center,
            PanelOverride = new StyleBoxFlat(Color.FromHex("#14212C")),
            MouseFilter = MouseFilterMode.Ignore,
        };
        var ranks = new BoxContainer { SeparationOverride = 4, Margin = new Thickness(8, 3) };
        ranks.AddChild(new Label
        {
            Name = "SkillLevel",
            Text = Loc.GetString("professional-skills-character-level", ("level", level)),
            FontColorOverride = color,
            Margin = new Thickness(0, 0, 6, 0),
        });
        for (var rank = 1; rank <= ProfessionalSkillRules.Maximum(skill); rank++)
        {
            ranks.AddChild(new PanelContainer
            {
                SetWidth = 7,
                SetHeight = 7,
                VerticalAlignment = VAlignment.Center,
                PanelOverride = new StyleBoxFlat(rank <= level ? color : Color.FromHex("#415363")),
                MouseFilter = MouseFilterMode.Ignore,
            });
        }
        badge.AddChild(ranks);
        header.Label.Parent!.AddChild(badge);

        var description = Text(Loc.GetString($"{ProfessionalSkillRules.NameKey(skill)}-level-{level}"));
        description.Name = "SkillDescription";
        description.Margin = new Thickness(14, 10, 12, 12);
        description.Modulate = Color.FromHex("#D6E2EB");
        var body = new CollapsibleBody();
        body.AddChild(description);
        var details = new Collapsible(header, body) { Name = $"Skill-{skill}" };
        var card = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#18232E"),
                BorderColor = Color.FromHex("#344858"),
                BorderThickness = new Thickness(1),
            },
        };
        card.AddChild(details);
        return card;
    }

    private static RichTextLabel Text(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }
}
