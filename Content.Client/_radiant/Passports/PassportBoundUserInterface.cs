using System.Numerics;
using Content.Client.Humanoid;
using Content.Shared._radiant.Dossiers;
using Content.Shared._radiant.Passports;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._radiant.Passports;

public sealed class PassportBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PassportWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new PassportWindow();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is PassportUiState passport)
            _window?.Update(passport);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _window?.Dispose();
        _window = null;
    }
}

public sealed class PassportWindow : DefaultWindow
{
    private static readonly Color Paper = Color.FromHex("#F3EBD8");
    private static readonly Color Ink = Color.FromHex("#252D38");
    private static readonly Color MutedInk = Color.FromHex("#606B76");
    private readonly IEntityManager _entities = IoCManager.Resolve<IEntityManager>();
    private readonly IPrototypeManager _prototypes = IoCManager.Resolve<IPrototypeManager>();
    private readonly BoxContainer _pages = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        HorizontalExpand = true,
        VerticalExpand = true,
        SeparationOverride = 0,
    };
    private EntityUid? _portraitDummy;

    public PassportWindow()
    {
        Title = Loc.GetString("passport-ui-title");
        MinWidth = 800;
        MinHeight = 480;
        SetSize = new Vector2(930, 550);
        Contents.AddChild(_pages);
    }

    public void Update(PassportUiState passport)
    {
        ClearPortrait();
        _pages.RemoveAllChildren();
        Title = Loc.GetString(passport.IsTemporary ? "passport-ui-temporary-title"
            : passport.Citizenship == RadiantCitizenship.NT ? "passport-ui-card-title" : "passport-ui-title");
        var accent = passport.IsTemporary ? Color.FromHex("#2C67AD") : PassportColor(passport.Citizenship);
        var nation = Loc.GetString($"radiant-citizenship-{passport.Citizenship.ToString().ToLowerInvariant()}");
        if (passport.Citizenship == RadiantCitizenship.NT && !passport.IsTemporary)
        {
            UpdateCorporateCard(passport, accent);
            return;
        }

        var cover = Panel(Ink, accent, 3);
        cover.HorizontalExpand = true;
        cover.VerticalExpand = true;
        _pages.AddChild(cover);
        var spread = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 0,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(7),
        };
        cover.AddChild(spread);

        var left = Panel(Paper, accent, 1);
        left.MinWidth = 380;
        left.HorizontalExpand = true;
        left.SizeFlagsStretchRatio = 1;
        var leftContent = Column(10, new Thickness(17));
        leftContent.VerticalExpand = true;
        left.AddChild(leftContent);
        spread.AddChild(left);

        var issuer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12,
            HorizontalExpand = true,
        };
        var printedSeal = passport.IsTemporary
            ? RadiantCitizenship.Asgard
            : passport.SealCitizenship ?? passport.Citizenship;
        var seal = new PassportSeal(passport.IsTemporary ? accent : PassportColor(printedSeal),
            printedSeal, passport.StarSeal, passport.IsTemporary)
        {
            MinWidth = 70,
            MinHeight = 70,
            SetSize = new Vector2(70, 70),
        };
        issuer.AddChild(seal);
        var issuerText = Column(2, new Thickness(0));
        issuerText.VerticalAlignment = Control.VAlignment.Center;
        issuerText.AddChild(new Label
        {
            Text = passport.IsTemporary ? Loc.GetString("passport-ui-temporary-issuer") : nation.ToUpperInvariant(),
            FontColorOverride = accent,
            StyleClasses = { "LabelHeading" },
        });
        issuerText.AddChild(new Label
        {
            Text = Loc.GetString(passport.IsTemporary ? "passport-ui-temporary-type" : "passport-ui-document-type"),
            FontColorOverride = MutedInk,
        });
        issuer.AddChild(issuerText);
        leftContent.AddChild(issuer);
        leftContent.AddChild(Rule(accent, 2));

        leftContent.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-photo"),
            FontColorOverride = accent,
        });
        var photoFrame = Panel(Color.FromHex("#B8C5C8"), accent, 2);
        photoFrame.SetSize = new Vector2(210, 154);
        photoFrame.RectClipContent = true;
        photoFrame.AddChild(CreatePortrait(passport));
        var centeredPhoto = new CenterContainer { HorizontalExpand = true };
        centeredPhoto.AddChild(photoFrame);
        leftContent.AddChild(centeredPhoto);
        leftContent.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-holder"),
            FontColorOverride = accent,
        });
        leftContent.AddChild(new Label
        {
            Text = Value(passport.OwnerName),
            FontColorOverride = Ink,
            HorizontalExpand = true,
        });
        leftContent.AddChild(Rule(accent, 2));
        leftContent.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-document-series", ("number", passport.Number)),
            FontColorOverride = MutedInk,
        });

        var spine = Panel(accent, Ink, 1);
        spine.MinWidth = 15;
        spine.AddChild(new Label { Text = "·\n·\n·\n·\n·", FontColorOverride = Paper });
        spread.AddChild(spine);

        var right = Panel(Color.FromHex("#FAF5E9"), accent, 1);
        right.HorizontalExpand = true;
        right.MinWidth = 380;
        right.SizeFlagsStretchRatio = 1;
        right.RectClipContent = true;
        AddWatermark(right, passport, accent);
        var rightContent = Column(8, new Thickness(19));
        rightContent.VerticalExpand = true;
        right.AddChild(rightContent);
        spread.AddChild(right);
        rightContent.AddChild(new Label
        {
            Text = Loc.GetString(passport.IsTemporary ? "passport-ui-temporary-type" : "passport-ui-document-type"),
            FontColorOverride = accent,
            StyleClasses = { "LabelHeading" },
        });
        rightContent.AddChild(Rule(accent, 2));
        rightContent.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-personal-data"),
            FontColorOverride = accent,
        });
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var fields = Column(9, new Thickness(0, 3));
        rightContent.AddChild(scroll);
        scroll.AddChild(fields);

        Field(fields, "passport-ui-field-number", passport.Number, accent);
        fields.AddChild(Rule(accent, 1));
        var species = _prototypes.TryIndex<SpeciesPrototype>(passport.Species, out var speciesPrototype)
            ? Loc.GetString(speciesPrototype.Name) : passport.Species;
        var sex = Enum.TryParse<Sex>(passport.Sex, out var parsedSex) ? parsedSex : Sex.Unsexed;
        Pair(fields, passport.IsTemporary ? "passport-ui-field-document-kind" : "passport-ui-field-citizenship",
            passport.IsTemporary ? Loc.GetString("passport-ui-temporary-type") : nation,
            "passport-ui-field-species", species, accent);
        Pair(fields, "passport-ui-field-sex", Loc.GetString($"ee-passport-sex-{sex.ToString().ToLowerInvariant()}"),
            "passport-ui-field-age", passport.Age > 0 ? passport.Age.ToString() : "", accent);
        Pair(fields, "passport-ui-field-height", passport.HeightCm > 0
                ? Loc.GetString("passport-ui-height-value", ("height", passport.HeightCm)) : "",
            "passport-ui-field-family", !string.IsNullOrWhiteSpace(passport.FamilyStatus)
                ? Loc.GetString(DossierFamilyStatus.LocalizationKey(passport.FamilyStatus, sex)) : "", accent);
        if (!string.IsNullOrWhiteSpace(passport.Residence))
            Field(fields, "passport-ui-field-residence", passport.Residence, accent);
        if (!string.IsNullOrWhiteSpace(passport.EmergencyContact))
            Field(fields, "passport-ui-field-emergency", passport.EmergencyContact, accent);
        rightContent.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-registration-mark"),
            FontColorOverride = MutedInk,
        });
        var machineZone = Panel(Paper, accent, 1);
        var machineLines = Column(1, new Thickness(8, 5));
        machineZone.AddChild(machineLines);
        machineLines.AddChild(new Label
        {
            Text = MachineLine(passport),
            FontColorOverride = Ink,
        });
        rightContent.AddChild(machineZone);
    }

    private static void AddWatermark(PanelContainer page, PassportUiState passport, Color accent)
    {
        var citizenship = passport.IsTemporary ? RadiantCitizenship.Asgard
            : passport.SealCitizenship ?? passport.Citizenship;
        var layer = new LayoutContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            InheritChildMeasure = false,
            MouseFilter = Control.MouseFilterMode.Ignore,
            RectClipContent = true,
        };
        var seal = new PassportSeal((passport.IsTemporary ? accent : PassportColor(citizenship)).WithAlpha(0.18f),
            citizenship, passport.StarSeal, passport.IsTemporary, stamped: true)
        {
            Name = "PassportWatermark",
            SetSize = new Vector2(270, 270),
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        LayoutContainer.SetAnchorLeft(seal, 0.64f);
        LayoutContainer.SetAnchorRight(seal, 0.64f);
        LayoutContainer.SetAnchorTop(seal, 0.56f);
        LayoutContainer.SetAnchorBottom(seal, 0.56f);
        LayoutContainer.SetMarginLeft(seal, -135);
        LayoutContainer.SetMarginRight(seal, 135);
        LayoutContainer.SetMarginTop(seal, -135);
        LayoutContainer.SetMarginBottom(seal, 135);
        layer.AddChild(seal);
        // Added before the content so ink never covers text or intercepts clicks.
        page.AddChild(layer);
    }

    private void UpdateCorporateCard(PassportUiState passport, Color accent)
    {
        var card = Panel(Color.FromHex("#E7EFF0"), accent, 4);
        card.HorizontalExpand = true;
        card.VerticalExpand = true;
        _pages.AddChild(card);
        AddWatermark(card, passport, accent);
        var body = Column(12, new Thickness(18));
        body.VerticalExpand = true;
        card.AddChild(body);
        var header = Panel(accent, accent, 0);
        var headerContent = Column(3, new Thickness(13, 8));
        headerContent.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-nt-issuer"),
            FontColorOverride = Color.White,
            StyleClasses = { "LabelHeading" },
        });
        headerContent.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-nt-card"),
            FontColorOverride = Color.White,
        });
        header.AddChild(headerContent);
        body.AddChild(header);

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 18,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        body.AddChild(content);
        var photoColumn = Column(7, new Thickness(0));
        photoColumn.MinWidth = 226;
        photoColumn.AddChild(new Label { Text = Loc.GetString("passport-ui-photo"), FontColorOverride = accent });
        var photoFrame = Panel(Color.FromHex("#B8C5C8"), accent, 2);
        photoFrame.SetSize = new Vector2(210, 154);
        photoFrame.RectClipContent = true;
        photoFrame.AddChild(CreatePortrait(passport));
        photoColumn.AddChild(photoFrame);
        photoColumn.AddChild(new Label { Text = Loc.GetString("passport-ui-holder"), FontColorOverride = accent });
        photoColumn.AddChild(new Label { Text = Value(passport.OwnerName), FontColorOverride = Ink });
        photoColumn.AddChild(Rule(accent, 2));
        photoColumn.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-document-series", ("number", passport.Number)),
            FontColorOverride = MutedInk,
        });
        content.AddChild(photoColumn);

        var detailsScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        content.AddChild(detailsScroll);
        var details = Column(10, new Thickness(0));
        detailsScroll.AddChild(details);
        details.AddChild(new Label
        {
            Text = Loc.GetString("passport-ui-personal-data"),
            FontColorOverride = accent,
            StyleClasses = { "LabelHeading" },
        });
        details.AddChild(Rule(accent, 2));
        Field(details, "passport-ui-field-number", passport.Number, accent);
        var species = _prototypes.TryIndex<SpeciesPrototype>(passport.Species, out var speciesPrototype)
            ? Loc.GetString(speciesPrototype.Name) : passport.Species;
        var sex = Enum.TryParse<Sex>(passport.Sex, out var parsedSex) ? parsedSex : Sex.Unsexed;
        Pair(details, "passport-ui-field-species", species, "passport-ui-field-age",
            passport.Age > 0 ? passport.Age.ToString() : "", accent);
        Pair(details, "passport-ui-field-sex", Loc.GetString($"ee-passport-sex-{sex.ToString().ToLowerInvariant()}"),
            "passport-ui-field-height", passport.HeightCm > 0
                ? Loc.GetString("passport-ui-height-value", ("height", passport.HeightCm)) : "", accent);
        Field(details, "passport-ui-field-family", !string.IsNullOrWhiteSpace(passport.FamilyStatus)
            ? Loc.GetString(DossierFamilyStatus.LocalizationKey(passport.FamilyStatus, sex)) : "", accent);
        if (!string.IsNullOrWhiteSpace(passport.Residence))
            Field(details, "passport-ui-field-residence", passport.Residence, accent);
        if (!string.IsNullOrWhiteSpace(passport.EmergencyContact))
            Field(details, "passport-ui-field-emergency", passport.EmergencyContact, accent);
        var machineZone = Panel(Color.FromHex("#D9E5E8"), accent, 1);
        machineZone.AddChild(new Label
        {
            Text = MachineLine(passport),
            FontColorOverride = Ink,
            Margin = new Thickness(10, 6),
        });
        body.AddChild(machineZone);
    }

    private Control CreatePortrait(PassportUiState passport)
    {
        if (passport.PhotoKind == PassportPhotoKind.Sketch)
            return new PassportSketchPortrait { SetSize = new Vector2(206, 150) };

        if (passport.Portrait == null || !_prototypes.TryIndex<SpeciesPrototype>(passport.Species, out var species))
            return new Label
            {
                Text = Loc.GetString("passport-ui-no-photo"),
                FontColorOverride = MutedInk,
                HorizontalExpand = true,
            };

        var sex = Enum.TryParse<Sex>(passport.Sex, out var parsed) ? parsed : Sex.Male;
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(passport.Species)
            .WithSex(sex)
            .WithAge(passport.Age)
            .WithCharacterAppearance(new HumanoidCharacterAppearance(passport.Portrait));
        _portraitDummy = _entities.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
        _entities.System<HumanoidAppearanceSystem>().LoadProfile(_portraitDummy.Value, profile);
        var uniform = _entities.SpawnEntity(PortraitUniform(passport), MapCoordinates.Nullspace);
        if (!_entities.System<InventorySystem>().TryEquip(_portraitDummy.Value, uniform, "jumpsuit", true, true))
            _entities.DeleteEntity(uniform);
        var portrait = new SpriteView
        {
            Scale = new Vector2(8, 8),
            Stretch = SpriteView.StretchMode.None,
            SetSize = new Vector2(320, 320),
            OverrideDirection = Direction.South,
        };
        portrait.SetEntity(_portraitDummy.Value);
        var crop = new LayoutContainer
        {
            SetSize = new Vector2(206, 150),
            RectClipContent = true,
            InheritChildMeasure = false,
        };
        var pattern = new PassportSecurityPattern(PassportColor(passport.IsTemporary
            ? RadiantCitizenship.Midgard : passport.Citizenship))
        {
            SetSize = new Vector2(206, 150),
        };
        crop.AddChild(pattern);
        // Humanoid sprites are offset inside their 320px preview control;
        // center the visible face in the 206px document photo, not the control itself.
        LayoutContainer.SetMarginLeft(portrait, 8);
        // SpriteView centers the full-body sprite; shift it inside the fixed crop
        // so the document shows the head and shoulders rather than the legs.
        LayoutContainer.SetMarginTop(portrait, 65);
        crop.AddChild(portrait);
        return crop;
    }

    private static void Pair(BoxContainer parent, string firstLabel, string firstValue,
        string secondLabel, string secondValue, Color accent)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 12,
        };
        Field(row, firstLabel, firstValue, accent);
        Field(row, secondLabel, secondValue, accent);
        parent.AddChild(row);
    }

    private static void Field(BoxContainer parent, string localization, string value, Color accent)
    {
        var group = Column(2, new Thickness(0, 0, 0, 4));
        group.AddChild(new Label { Text = Loc.GetString(localization), FontColorOverride = MutedInk });
        var text = new RichTextLabel { HorizontalExpand = true, MaxWidth = 460 };
        text.SetMessage(Value(value), defaultColor: Ink);
        group.AddChild(text);
        parent.AddChild(group);
    }

    private static string Value(string value)
        => string.IsNullOrWhiteSpace(value) ? Loc.GetString("passport-ui-unfilled") : value;

    private static string MachineLine(PassportUiState passport)
    {
        var country = passport.IsTemporary ? "TMP" : passport.Citizenship switch
        {
            RadiantCitizenship.Asgard => "ASG",
            RadiantCitizenship.Hesperia => "HES",
            RadiantCitizenship.Aurum => "AUR",
            RadiantCitizenship.Zon => "ZON",
            RadiantCitizenship.Midgard => "MID",
            RadiantCitizenship.Avrelia => "AVR",
            RadiantCitizenship.NT => "NANOTRASEN",
            _ => "ASG",
        };
        var name = passport.OwnerName.ToUpperInvariant().Replace(' ', '<');
        var serial = passport.Number.Replace("-", "");
        var prefix = passport.Citizenship == RadiantCitizenship.NT && !passport.IsTemporary ? "I" : "P";
        var header = $"{prefix}<{country}<<";
        var tail = $"<{serial}";
        var nameLength = Math.Max(0, 42 - header.Length - tail.Length);
        if (name.Length > nameLength)
            name = name[..nameLength];
        return (header + name + tail).PadRight(42, '<');
    }

    private static string PortraitUniform(PassportUiState passport) => passport.IsTemporary
        ? "ClothingUniformJumpsuitCentcomAgent"
        : passport.Citizenship switch
        {
            RadiantCitizenship.Asgard => "ClothingUniformJumpsuitSuitRed",
            RadiantCitizenship.Hesperia => "ClothingUniformJumpsuitQMFormal",
            RadiantCitizenship.Aurum => "ClothingUniformJumpsuitCapFormal",
            RadiantCitizenship.Zon => "ClothingUniformJumpsuitCentcomFormal",
            RadiantCitizenship.Midgard => "ClothingUniformJumpsuitCentcomOfficer",
            RadiantCitizenship.Avrelia => "ClothingUniformJumpsuitScientistFormal",
            RadiantCitizenship.NT => "ClothingUniformJumpsuitNanotrasen",
            _ => "ClothingUniformJumpsuitCapFormal",
        };

    private static Color PassportColor(RadiantCitizenship citizenship) => citizenship switch
    {
        RadiantCitizenship.Asgard => Color.FromHex("#A9414A"),
        RadiantCitizenship.Hesperia => Color.FromHex("#A66418"),
        RadiantCitizenship.Aurum => Color.FromHex("#A47418"),
        RadiantCitizenship.Zon => Color.FromHex("#1A7558"),
        RadiantCitizenship.Midgard => Color.FromHex("#2C67AD"),
        RadiantCitizenship.Avrelia => Color.FromHex("#167F85"),
        RadiantCitizenship.NT => Color.FromHex("#257C9A"),
        _ => Color.FromHex("#2C67AD"),
    };

    private static PanelContainer Rule(Color color, int height)
    {
        var rule = Panel(color, color, 0);
        rule.MinHeight = height;
        return rule;
    }

    private static PanelContainer Panel(Color background, Color border, int thickness) => new()
    {
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(thickness),
        },
    };

    private static BoxContainer Column(int spacing, Thickness margin) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = spacing,
        Margin = margin,
        HorizontalExpand = true,
    };

    private void ClearPortrait()
    {
        if (_portraitDummy is { } dummy && _entities.EntityExists(dummy))
            _entities.DeleteEntity(dummy);
        _portraitDummy = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ClearPortrait();
        base.Dispose(disposing);
    }
}

public sealed class PassportSeal(Color ink, RadiantCitizenship citizenship, bool starSeal,
    bool confederationSeal = false, bool stamped = false) : Control
{
    public Color InkColor => ink;
    protected override void Draw(IRenderHandle handle)
    {
        var center = PixelSize / 2;
        var scale = MathF.Min(PixelSize.X, PixelSize.Y) / 64f;
        var screen = handle.DrawingHandleScreen;
        var sealInk = stamped ? ink.WithAlpha(ink.A * 0.8f) : ink;
        var angleOffset = stamped ? -MathF.PI / 24 : 0;
        Vector2 Point(float x, float y) => center + new Vector2(
            x * MathF.Cos(angleOffset) - y * MathF.Sin(angleOffset),
            x * MathF.Sin(angleOffset) + y * MathF.Cos(angleOffset)) * scale;
        void Line(float x1, float y1, float x2, float y2)
        {
            var from = Point(x1, y1);
            var to = Point(x2, y2);
            if (!stamped)
            {
                screen.DrawLine(from, to, sealInk);
                return;
            }
            var edge = to - from;
            if (edge.LengthSquared() < 0.001f)
                return;
            var normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X)) * (0.26f * scale);
            screen.DrawPrimitives(DrawPrimitiveTopology.TriangleFan,
                new[] { from + normal, to + normal, to - normal, from - normal }, sealInk);
        }
        void Quad(float x1, float y1, float x2, float y2,
            float x3, float y3, float x4, float y4)
            => screen.DrawPrimitives(DrawPrimitiveTopology.TriangleFan,
                new[] { Point(x1, y1), Point(x2, y2), Point(x3, y3), Point(x4, y4) }, sealInk);
        void Ring(float radius)
        {
            if (!stamped)
            {
                screen.DrawCircle(center, radius * scale, sealInk, false);
                return;
            }
            for (var index = 0; index < 128; index++)
            {
                // Fixed breaks resemble uneven ink, without changing between redraws.
                if (index % 31 is 4 or 5 || index % 47 == 11)
                    continue;
                var from = index * MathF.Tau / 128;
                var to = (index + 1) * MathF.Tau / 128;
                Line(MathF.Cos(from) * radius, MathF.Sin(from) * radius,
                    MathF.Cos(to) * radius, MathF.Sin(to) * radius);
            }
        }
        void Star(float radius)
        {
            for (var index = 0; index < 5; index++)
            {
                var from = -MathF.PI / 2 + index * MathF.Tau / 5;
                var to = -MathF.PI / 2 + ((index + 2) % 5) * MathF.Tau / 5;
                Line(MathF.Cos(from) * radius, MathF.Sin(from) * radius,
                    MathF.Cos(to) * radius, MathF.Sin(to) * radius);
            }
        }

        Ring(29);
        Ring(25);
        if (stamped)
        {
            for (var index = 0; index < 40; index++)
            {
                var angle = index * MathF.Tau / 40;
                Line(MathF.Cos(angle) * 26.5f, MathF.Sin(angle) * 26.5f,
                    MathF.Cos(angle) * 27.5f, MathF.Sin(angle) * 27.5f);
            }
        }
        if (starSeal)
        {
            Star(17);
            screen.DrawCircle(center, 3 * scale, ink, true);
            return;
        }
        if (confederationSeal)
        {
            screen.DrawCircle(center, 15 * scale, ink, false);
            for (var index = 0; index < 3; index++)
            {
                var angle = -MathF.PI / 2 + index * MathF.Tau / 3;
                screen.DrawCircle(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 15 * scale,
                    4 * scale, ink, false);
            }
            return;
        }
        switch (citizenship)
        {
            case RadiantCitizenship.Asgard:
                Line(-16, 17, 15, -16);
                Line(-15, -16, 16, 17);
                Quad(-4, -4, 0, -10, 4, -4, 0, 3);
                break;
            case RadiantCitizenship.Hesperia:
                screen.DrawCircle(center, 16 * scale, ink, false);
                screen.DrawCircle(center, 5 * scale, ink, true);
                screen.DrawCircle(Point(-16, 0), 3 * scale, ink, true);
                screen.DrawCircle(Point(16, 0), 3 * scale, ink, true);
                break;
            case RadiantCitizenship.Aurum:
                Line(-17, 11, 17, 11);
                Line(-17, 15, 17, 15);
                Line(-17, 11, -16, -8);
                Line(-16, -8, -7, 1);
                Line(-7, 1, 0, -15);
                Line(0, -15, 7, 1);
                Line(7, 1, 16, -8);
                Line(16, -8, 17, 11);
                break;
            case RadiantCitizenship.Zon:
                Line(0, 18, 0, -15);
                Line(0, 7, -13, -7);
                Line(0, 1, 13, -13);
                Line(-13, -7, -16, -16);
                Line(13, -13, 16, -20);
                screen.DrawCircle(Point(0, -17), 4 * scale, ink, true);
                break;
            case RadiantCitizenship.Midgard:
                screen.DrawCircle(center, 17 * scale, ink, false);
                Line(-17, 0, 17, 0);
                Line(0, -17, 0, 17);
                Line(-13, -8, 13, -8);
                Line(-13, 8, 13, 8);
                break;
            case RadiantCitizenship.Avrelia:
                Line(-18, -8, -9, -13);
                Line(-9, -13, 0, -8);
                Line(0, -8, 9, -13);
                Line(9, -13, 18, -8);
                Line(-18, 3, -9, -2);
                Line(-9, -2, 0, 3);
                Line(0, 3, 9, -2);
                Line(9, -2, 18, 3);
                Line(-18, 14, -9, 9);
                Line(-9, 9, 0, 14);
                Line(0, 14, 9, 9);
                Line(9, 9, 18, 14);
                break;
            case RadiantCitizenship.NT:
                Line(-16, -15, -16, 15);
                Line(-16, -15, 0, 5);
                Line(0, 5, 0, -15);
                Line(5, -15, 19, -15);
                Line(12, -15, 12, 15);
                break;
        }
    }
}

public sealed class PassportSecurityPattern(Color ink) : Control
{
    protected override void Draw(IRenderHandle handle)
    {
        var screen = handle.DrawingHandleScreen;
        var pale = ink.WithAlpha(0.13f);
        var step = 16 * UIScale;
        for (float x = -PixelSize.Y; x < PixelSize.X + PixelSize.Y; x += step)
            screen.DrawLine(new Vector2(x, 0), new Vector2(x + PixelSize.Y, PixelSize.Y), pale);
        screen.DrawCircle(PixelSize / 2, 54 * UIScale, ink.WithAlpha(0.08f), false);
    }
}


public sealed class PassportSketchPortrait : Control
{
    protected override void Draw(IRenderHandle handle)
    {
        var screen = handle.DrawingHandleScreen;
        var ink = Color.FromHex("#574F48");
        Vector2 Point(float x, float y) => new(x * PixelSize.X / 206f, y * PixelSize.Y / 150f);
        void Line(float x1, float y1, float x2, float y2)
            => screen.DrawLine(Point(x1, y1), Point(x2, y2), ink);

        screen.DrawCircle(Point(103, 59), 31 * PixelSize.X / 206f, ink, false);
        Line(72, 51, 81, 29);
        Line(81, 29, 90, 39);
        Line(90, 39, 101, 25);
        Line(101, 25, 112, 37);
        Line(112, 37, 124, 29);
        Line(124, 29, 134, 52);
        screen.DrawCircle(Point(91, 58), 3 * PixelSize.X / 206f, ink);
        screen.DrawCircle(Point(115, 59), 3 * PixelSize.X / 206f, ink);
        Line(103, 61, 99, 72);
        Line(99, 72, 106, 74);
        Line(91, 82, 105, 86);
        Line(105, 86, 118, 81);
        Line(80, 85, 67, 112);
        Line(67, 112, 42, 123);
        Line(42, 123, 34, 150);
        Line(126, 85, 140, 110);
        Line(140, 110, 164, 123);
        Line(164, 123, 173, 150);
        Line(79, 116, 103, 133);
        Line(103, 133, 128, 116);
    }
}
