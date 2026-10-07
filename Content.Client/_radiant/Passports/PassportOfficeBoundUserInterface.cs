using System.Linq;
using Content.Shared._radiant.Passports;
using Content.Shared._radiant.Dossiers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._radiant.Passports;

public sealed class PassportOfficeBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PassportOfficeWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PassportOfficeWindow>();
        _window.Refresh += () => SendMessage(new PassportOfficeRefreshMessage());
        _window.Issue += message => SendMessage(message);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is PassportOfficeUiState office)
            _window?.Update(office);
    }
}

public sealed class PassportOfficeWindow : DefaultWindow
{
    private readonly BoxContainer _people = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 8,
        HorizontalExpand = true,
    };
    private readonly BoxContainer _form = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 7,
        HorizontalExpand = true,
    };

    public event Action? Refresh;
    public event Action<PassportOfficeIssueMessage>? Issue;

    public PassportOfficeWindow()
    {
        Title = Loc.GetString("radiant-passport-office-title");
        MinWidth = 640;
        MinHeight = 520;
        SetSize = new System.Numerics.Vector2(700, 650);

        var contents = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10,
            Margin = new Thickness(12),
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        contents.AddChild(new Label
        {
            Text = Loc.GetString("radiant-passport-office-description"),
            FontColorOverride = Color.FromHex("#AEBFCC"),
        });
        var refresh = new Button { Text = Loc.GetString("radiant-passport-office-refresh") };
        refresh.OnPressed += _ => Refresh?.Invoke();
        contents.AddChild(refresh);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_people);
        contents.AddChild(scroll);
        contents.AddChild(_form);
        Contents.AddChild(contents);
    }

    public void Update(PassportOfficeUiState state)
    {
        _people.RemoveAllChildren();
        _form.RemoveAllChildren();
        if (state.Candidates.Count == 0)
        {
            _people.AddChild(new Label { Text = Loc.GetString("radiant-passport-office-empty") });
            return;
        }

        foreach (var (target, applicant) in state.Candidates.OrderBy(pair => pair.Value.Name))
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 8, HorizontalExpand = true };
            row.AddChild(new Label { Text = applicant.Name, HorizontalExpand = true });
            var select = new Button { Text = Loc.GetString("radiant-passport-office-select") };
            select.OnPressed += _ => ShowForm(target, applicant);
            row.AddChild(select);
            _people.AddChild(row);
        }
    }

    private void ShowForm(NetEntity target, PassportOfficeApplicant applicant)
    {
        _form.RemoveAllChildren();
        _form.AddChild(new Label { Text = Loc.GetString("radiant-passport-office-form-title") });
        _form.AddChild(new Label
        {
            Text = Loc.GetString("radiant-passport-office-biometric", ("species", applicant.Species),
                ("sex", applicant.Sex)),
            FontColorOverride = Color.FromHex("#AEBFCC"),
        });
        var name = Field("radiant-passport-office-name", applicant.Name);
        var age = Field("radiant-passport-office-age", applicant.Age.ToString());
        var height = Field("radiant-passport-office-height", applicant.Height.ToString());
        var residence = Field("radiant-passport-office-residence", applicant.Residence);
        var emergency = Field("radiant-passport-office-emergency", applicant.EmergencyContact);
        _form.AddChild(new Label { Text = Loc.GetString("radiant-passport-office-family") });
        var family = new OptionButton { HorizontalExpand = true };
        for (var index = 0; index < DossierFamilyStatus.Values.Length; index++)
            family.AddItem(Loc.GetString(DossierFamilyStatus.LocalizationKey(
                DossierFamilyStatus.Values[index], applicant.SexValue)), index);
        family.SelectId(Math.Max(0, Array.IndexOf(DossierFamilyStatus.Values,
            DossierFamilyStatus.Normalize(applicant.FamilyStatus))));
        family.OnItemSelected += args => family.SelectId(args.Id);
        _form.AddChild(family);
        var issue = new Button { Text = Loc.GetString("radiant-passport-office-issue"), HorizontalExpand = true };
        issue.OnPressed += _ =>
        {
            if (!int.TryParse(age.Text, out var parsedAge) || !int.TryParse(height.Text, out var parsedHeight))
                return;
            Issue?.Invoke(new PassportOfficeIssueMessage(target, name.Text, parsedAge, parsedHeight,
                residence.Text, emergency.Text, DossierFamilyStatus.Values[family.SelectedId]));
        };
        _form.AddChild(issue);
    }

    private LineEdit Field(string label, string value)
    {
        _form.AddChild(new Label { Text = Loc.GetString(label) });
        var input = new LineEdit { Text = value, HorizontalExpand = true };
        _form.AddChild(input);
        return input;
    }
}
