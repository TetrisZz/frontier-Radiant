using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.RichText;
using Content.Shared._radiant.Paperwork;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Paperwork;

public sealed class PhotocopierBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PhotocopierWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PhotocopierWindow>();
        if (_window.Parent is { } parent)
        {
            _window.SetSize = Vector2.Max(_window.MinSize, Vector2.Min(_window.SetSize, parent.Size));
            _window.Measure(parent.Size);
            LayoutContainer.SetPosition(_window, Vector2.Clamp(_window.Position, Vector2.Zero,
                Vector2.Max(Vector2.Zero, parent.Size - _window.DesiredSize)));
        }
        _window.Print += (id, count) => SendMessage(new PhotocopierPrintMessage(id, count));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is PhotocopierUiState printer)
            _window?.Update(printer);
    }
}

public sealed class PhotocopierWindow : FancyWindow
{
    public event Action<string?, int>? Print;
    private readonly BoxContainer _forms = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
    private readonly Dictionary<string, ContainerButton> _formButtons = new();
    private readonly BoxContainer _sidebar = new() { Name = "PhotocopierSidebar", Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 300, MaxWidth = 300, SeparationOverride = 6 };
    private readonly LineEdit _search = new() { PlaceHolder = Loc.GetString("photocopier-search") };
    private readonly RichTextLabel _preview = new() { Name = "PhotocopierPreview", HorizontalExpand = true, VerticalAlignment = Control.VAlignment.Top, Margin = new Thickness(12) };
    private readonly Label _supplies = new() { ClipText = true };
    private readonly SpinBox _copies = new() { MinWidth = 80 };
    private readonly Button _print = new() { Name = "PhotocopierPrint", Text = Loc.GetString("photocopier-print"), HorizontalExpand = true, MinHeight = 34 };
    private readonly Button _copy = new() { Name = "PhotocopierCopy", Text = Loc.GetString("photocopier-copy"), HorizontalExpand = true, MinHeight = 34 };
    private PhotocopierUiState? _state;
    private string? _selected;

    public PhotocopierWindow()
    {
        Title = Loc.GetString("photocopier-title");
        MinSize = new Vector2(720, 440);
        SetSize = new Vector2(940, 640);
        var content = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(8) };
        ContentsContainer.AddChild(content);
        content.AddChild(_supplies);
        var columns = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 12 };
        content.AddChild(columns);
        _sidebar.AddChild(_search);
        var list = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        list.AddChild(_forms);
        _sidebar.AddChild(list);
        columns.AddChild(_sidebar);
        var previewPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#E5E9E9") },
        };
        var previewScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true };
        previewScroll.AddChild(_preview);
        previewPanel.AddChild(previewScroll);
        columns.AddChild(previewPanel);
        var quantity = new BoxContainer { SeparationOverride = 8 };
        quantity.AddChild(new Label { Text = Loc.GetString("photocopier-copies") });
        _copies.IsValid = value => value is >= 1 and <= 5;
        _copies.Value = 1;
        quantity.AddChild(_copies);
        content.AddChild(quantity);
        var actions = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8 };
        actions.AddChild(_copy);
        actions.AddChild(_print);
        content.AddChild(actions);
        _search.OnTextChanged += _ => RenderForms();
        _print.OnPressed += _ => { if (_selected != null) Print?.Invoke(_selected, _copies.Value); };
        _copy.OnPressed += _ => Print?.Invoke(null, _copies.Value);
        _copies.ValueChanged += _ => RefreshActions();
        RefreshActions();
    }

    public void Update(PhotocopierUiState state)
    {
        var formsChanged = _state == null || !_state.Forms.SequenceEqual(state.Forms);
        _state = state;
        _supplies.Text = Loc.GetString("photocopier-supplies", ("paper", state.Paper), ("ink", state.Ink));
        if (formsChanged)
        {
            if (state.Forms.All(f => f.Id != _selected))
                _selected = state.Forms.FirstOrDefault(f => f.Id == "GeneralDocument")?.Id ?? state.Forms.FirstOrDefault()?.Id;
            RenderForms();
            if (state.Forms.FirstOrDefault(f => f.Id == _selected) is { } selected)
                SelectForm(selected);
            else
                _preview.Clear();
        }
        RefreshActions();
    }

    private void RenderForms()
    {
        _forms.DisposeAllChildren();
        _formButtons.Clear();
        if (_state == null)
            return;
        foreach (var group in _state.Forms.Where(f => f.Name.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase)).GroupBy(f => f.Category))
        {
            var category = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = Control.VAlignment.Top, Margin = new Thickness(0, 8, 0, 4) };
            category.SetMessage(FormattedMessage.FromUnformatted(group.Key));
            _forms.AddChild(category);
            foreach (var form in group)
            {
                var button = new ContainerButton { Name = "PhotocopierForm", ToolTip = form.Name, HorizontalExpand = true,
                    ToggleMode = true, Pressed = form.Id == _selected, MinHeight = 36, StyleClasses = { "Button" } };
                var label = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = Control.VAlignment.Top, Margin = new Thickness(8, 6) };
                label.SetMessage(FormattedMessage.FromUnformatted(form.Name));
                button.AddChild(label);
                button.OnPressed += _ => SelectForm(form);
                _formButtons.Add(form.Id, button);
                _forms.AddChild(button);
            }
        }
    }

    private void SelectForm(DocumentFormEntry form)
    {
        _selected = form.Id;
        foreach (var (id, button) in _formButtons)
            button.Pressed = id == _selected;
        _preview.SetMessage(FormattedMessage.FromMarkupPermissive(form.Content),
            [typeof(BoldTag), typeof(ItalicTag), typeof(ColorTag), typeof(HeadingTag), typeof(MonoTag), typeof(FormTagHandler)],
            Color.FromHex("#252B2F"));
        DisablePreviewFields(_preview);
        RefreshActions();
    }

    private static void DisablePreviewFields(Control control)
    {
        if (control is PaperFormButton field)
            field.Disabled = true;
        foreach (var child in control.Children)
            DisablePreviewFields(child);
    }

    private void RefreshActions()
    {
        var ready = _state is { Ready: true } state && state.Paper >= _copies.Value && state.Ink >= _copies.Value;
        _print.Disabled = !ready || _selected == null;
        _copy.Disabled = !ready || _state?.Original != true;
    }
}
