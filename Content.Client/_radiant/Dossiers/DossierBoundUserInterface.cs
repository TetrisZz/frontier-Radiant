using System.Collections.Generic;
using System.Linq;
using Content.Shared._radiant.Dossiers;
using Content.Shared.Humanoid;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Dossiers;

public sealed class DossierBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DossierWindow? _window;
    private readonly Dictionary<NetEntity, DossierViewerAccessMessage> _viewerAccess = new();
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DossierWindow>();
        _window.Select += target =>
        {
            _viewerAccess.Clear();
            SendMessage(new DossierSelectMessage(target));
        };
        _window.Edit += (target, field, value) => SendMessage(new DossierEditMessage(target, field, value));
        _window.Print += target => SendMessage(new DossierPrintMessage(target));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is DossierUiState dossier)
            _window?.Update(dossier, dossier.Selected is { } selected && _viewerAccess.TryGetValue(selected, out var access)
                ? access
                : null);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is not DossierViewerAccessMessage access)
            return;
        _viewerAccess[access.Selected] = access;
        if (State is DossierUiState dossier && dossier.Selected == access.Selected)
            _window?.Update(dossier, access);
    }
}

public sealed class DossierWindow : DefaultWindow
{
    private readonly BoxContainer _people = new() { Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 6, Margin = new Thickness(10) };
    private readonly BoxContainer _details = new() { Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 10, Margin = new Thickness(12), HorizontalExpand = true };
    public event Action<NetEntity>? Select;
    public event Action<NetEntity, DossierField, string>? Edit;
    public event Action<NetEntity>? Print;
    public DossierWindow()
    {
        MinWidth = 780;
        MinHeight = 580;
        SetSize = new System.Numerics.Vector2(1000, 740);
        var columns = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10, HorizontalExpand = true, VerticalExpand = true };
        var listPanel = new PanelContainer
        {
            MinWidth = 235,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#151D28"),
                BorderColor = Color.FromHex("#35566B"), BorderThickness = new Thickness(1) },
        };
        var listScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        listScroll.AddChild(_people);
        listPanel.AddChild(listScroll);
        columns.AddChild(listPanel);
        var detailPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#222831"),
                BorderColor = Color.FromHex("#35566B"), BorderThickness = new Thickness(1) },
        };
        var detailScroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        detailScroll.AddChild(_details);
        detailPanel.AddChild(detailScroll);
        columns.AddChild(detailPanel);
        Contents.AddChild(columns);
    }
    public void Update(DossierUiState state, DossierViewerAccessMessage? viewerAccess)
    {
        Title = Loc.GetString(state.Kind switch
        {
            DossierKind.Medical => "radiant-dossier-medical",
            DossierKind.Security => "radiant-dossier-security",
            _ => "radiant-dossier-personal",
        });
        _people.RemoveAllChildren();
        _details.RemoveAllChildren();
        _people.AddChild(new Label { Text = Loc.GetString("radiant-dossier-registry"),
            FontColorOverride = Color.FromHex("#66BDD9") });
        foreach (var (target, name) in state.People.OrderBy(p => p.Value))
        {
            var button = new Button { Text = target == state.Selected ? $"● {name}" : name, HorizontalExpand = true };
            button.OnPressed += _ => Select?.Invoke(target);
            _people.AddChild(button);
        }
        if (state.Selected is not { } selected || state.Record is not { } record)
        {
            _details.AddChild(new Label { Text = Loc.GetString("radiant-dossier-select") });
            return;
        }
        var header = Section(_details, "radiant-dossier-record-section");
        header.AddChild(new Label { Text = state.Name, FontColorOverride = Color.FromHex("#8CD4E9") });
        if (state.Kind != DossierKind.Personal)
            ReadOnly(header, "radiant-dossier-passport-number", state.PassportNumber);
        var print = new Button { Text = Loc.GetString("radiant-dossier-print") };
        print.OnPressed += _ => Print?.Invoke(selected);
        header.AddChild(print);
        var canEdit = viewerAccess is { IsOwnRecord: false } && state.Kind != DossierKind.Personal;
        if (state.Kind == DossierKind.Personal)
        {
            if (!string.IsNullOrWhiteSpace(state.BasicDetails))
                header.AddChild(new Label { Text = state.BasicDetails, FontColorOverride = Color.FromHex("#B2BEC8") });
            var identity = Section(_details, "radiant-dossier-identity-section");
            ReadOnly(identity, "radiant-dossier-passport-number", state.PassportNumber);
            ReadOnly(identity, "radiant-dossier-birthplace", record.Birthplace);
            ReadOnly(identity, "radiant-dossier-residence", record.Residence);
            ReadOnly(identity, "radiant-dossier-family",
                Loc.GetString(DossierFamilyStatus.LocalizationKey(record.FamilyStatus, (Sex) state.Sex)));
            ReadOnly(identity, "radiant-dossier-occupation", record.Occupation);
            ReadOnly(identity, "radiant-dossier-education", record.Education);
            var contacts = Section(_details, "radiant-dossier-contacts-section");
            ReadOnly(contacts, "radiant-dossier-emergency", record.EmergencyContact);
            ReadOnly(contacts, "radiant-dossier-features", record.DistinguishingFeatures);
            var health = Section(_details, "radiant-dossier-self-reported-section");
            ReadOnly(health, "radiant-dossier-blood-group", record.BloodGroup);
            ReadOnly(health, "radiant-dossier-allergies", record.Allergies);
            ReadOnly(health, "radiant-dossier-medical-history", record.MedicalHistory);
            return;
        }
        if (state.Kind == DossierKind.Security)
        {
            var security = Section(_details, "radiant-dossier-security-section");
            Editable(security, selected, DossierField.SecurityPermissions, "radiant-dossier-permissions", record.SecurityPermissions, canEdit);
            Editable(security, selected, DossierField.SecurityArrests, "radiant-dossier-arrests", record.SecurityArrests, canEdit);
            Editable(security, selected, DossierField.SecurityConvictions, "radiant-dossier-convictions", record.SecurityConvictions, canEdit);
            if (viewerAccess?.SecurityNotes is { } notes)
                Editable(security, selected, DossierField.SecurityNotes, "radiant-dossier-security-notes", notes, canEdit);
            LastEdit(_details, record.LastSecurityEditor);
            return;
        }
        var medical = Section(_details, "radiant-dossier-medical-section");
        Editable(medical, selected, DossierField.MedicalInstructions, "radiant-dossier-instructions", record.MedicalInstructions, canEdit);
        Editable(medical, selected, DossierField.MedicalRestrictions, "radiant-dossier-restrictions", record.MedicalRestrictions, canEdit);
        Editable(medical, selected, DossierField.MedicalPhysiology, "radiant-dossier-physiology", record.MedicalPhysiology, canEdit);
        Editable(medical, selected, DossierField.MedicalPsychology, "radiant-dossier-psychology", record.MedicalPsychology, canEdit);
        Editable(medical, selected, DossierField.MedicalNotes, "radiant-dossier-medical-notes", record.MedicalNotes, canEdit);
        LastEdit(_details, record.LastMedicalEditor);
    }
    private static BoxContainer Section(BoxContainer parent, string title)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1B2330"),
                BorderColor = Color.FromHex("#41647A"), BorderThickness = new Thickness(1) },
        };
        var contents = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6, HorizontalExpand = true, Margin = new Thickness(10) };
        contents.AddChild(new Label { Text = Loc.GetString(title), FontColorOverride = Color.FromHex("#66BDD9") });
        panel.AddChild(contents);
        parent.AddChild(panel);
        return contents;
    }
    private void ReadOnly(BoxContainer parent, string label, string value)
    {
        parent.AddChild(new Label { Text = Loc.GetString(label), FontColorOverride = Color.FromHex("#A2B3C2") });
        var text = new RichTextLabel { HorizontalExpand = true };
        text.SetMessage(!string.IsNullOrWhiteSpace(value) ? value : Loc.GetString("radiant-dossier-empty"));
        parent.AddChild(text);
    }
    private void Editable(BoxContainer parent, NetEntity selected, DossierField field, string label, string value, bool canEdit)
    {
        if (!canEdit)
        {
            ReadOnly(parent, label, value);
            return;
        }
        parent.AddChild(new Label { Text = Loc.GetString(label), FontColorOverride = Color.FromHex("#A2B3C2") });
        var input = new TextEdit { MinHeight = 65, HorizontalExpand = true,
            TextRope = new Rope.Leaf(value) };
        parent.AddChild(input);
        var save = new Button { Text = Loc.GetString("radiant-dossier-save") };
        save.OnPressed += _ => Edit?.Invoke(selected, field, Rope.Collapse(input.TextRope));
        parent.AddChild(save);
    }
    private void LastEdit(BoxContainer parent, string editor)
    {
        if (!string.IsNullOrWhiteSpace(editor))
            parent.AddChild(new Label { Text = Loc.GetString("radiant-dossier-last-edit", ("name", editor)) });
    }
}
