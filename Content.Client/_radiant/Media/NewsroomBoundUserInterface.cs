using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._radiant.Media;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Media;

public sealed class NewsroomBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private NewsroomWindow? _window;
    protected override void Open()
    {
        base.Open(); _window = new NewsroomWindow(); _window.OnClose += Close;
        _window.Action += SendMessage; Update<NewsroomUiState>(); _window.OpenCentered();
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    { base.UpdateState(state); if (state is NewsroomUiState data) _window?.UpdateState(data); }
    protected override void Dispose(bool disposing)
    { base.Dispose(disposing); if (disposing) { _window?.Dispose(); _window = null; } }
}

public sealed class NewsroomWindow : FancyWindow
{
    public event Action<NewsroomActionMessage>? Action;
    private readonly LineEdit _channel = new() { Name = "NewsChannel", HorizontalExpand = true };
    private readonly LineEdit _title = new() { Name = "NewsTitle", HorizontalExpand = true };
    private readonly TextEdit _content = new() { Name = "NewsBody", MinHeight = 150, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _articles = CivicUi.Column();
    private readonly BoxContainer _preview = CivicUi.Column();
    private readonly RichTextLabel _status = new() { HorizontalExpand = true };
    private readonly RichTextLabel _photo = new() { HorizontalExpand = true };
    private readonly Button _publish;
    private readonly Button _print;
    private string _listKey = "";
    private Guid? _selected;
    private bool _initialized;
    public NewsroomWindow()
    {
        Title = Loc.GetString("newsroom-ui-title"); MinSize = new Vector2(720, 520); SetSize = new Vector2(900, 740);
        var column = CivicUi.Column(); column.Margin = new Thickness(8);
        column.AddChild(CivicUi.Card(Loc.GetString("newsroom-ui-header"), CivicUi.Text(Loc.GetString("newsroom-ui-help"))));
        _channel.PlaceHolder = Loc.GetString("newsroom-ui-channel-placeholder");
        _title.PlaceHolder = Loc.GetString("newsroom-ui-title-placeholder");
        _content.Placeholder = new Rope.Leaf(Loc.GetString("newsroom-ui-body-placeholder"));
        var channelRow = new BoxContainer { HorizontalExpand = true, SeparationOverride = 6 };
        channelRow.AddChild(new Label { Text = Loc.GetString("newsroom-ui-channel") }); channelRow.AddChild(_channel);
        var choose = new Button { Text = Loc.GetString("newsroom-ui-open-channel") };
        choose.OnPressed += _ => Action?.Invoke(new(NewsroomAction.Channel, channel: _channel.Text)); channelRow.AddChild(choose); column.AddChild(channelRow);
        var reading = new BoxContainer { VerticalExpand = true, HorizontalExpand = true, SeparationOverride = 8, MinHeight = 220 };
        var listColumn = CivicUi.Column();
        listColumn.AddChild(new Label { Text = Loc.GetString("newsroom-ui-article-list") });
        listColumn.AddChild(_articles);
        var list = new ScrollContainer { MinWidth = 260, MaxWidth = 320, VerticalExpand = true, HScrollEnabled = false };
        list.AddChild(listColumn); reading.AddChild(list);
        var previewColumn = CivicUi.Column();
        previewColumn.AddChild(new Label { Text = Loc.GetString("newsroom-ui-preview") });
        var preview = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
        preview.AddChild(_preview); previewColumn.AddChild(preview); reading.AddChild(previewColumn); column.AddChild(reading);
        var editor = CivicUi.Column();
        editor.AddChild(new Label { Text = Loc.GetString("newsroom-ui-title-field") }); editor.AddChild(_title);
        editor.AddChild(new Label { Text = Loc.GetString("newsroom-ui-body-field") }); editor.AddChild(_content);
        var photoRow = new BoxContainer { SeparationOverride = 8 }; photoRow.AddChild(_photo);
        var clear = new Button { Text = Loc.GetString("newsroom-ui-clear-photo") };
        clear.OnPressed += _ => Action?.Invoke(new(NewsroomAction.ClearPhoto)); photoRow.AddChild(clear); editor.AddChild(photoRow);
        column.AddChild(CivicUi.Card(Loc.GetString("newsroom-ui-editor"), editor));
        column.AddChild(_status);
        var buttons = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8 };
        _publish = new Button { Text = Loc.GetString("newsroom-ui-publish"), HorizontalExpand = true };
        _publish.OnPressed += _ => Action?.Invoke(new(NewsroomAction.Publish, channel: _channel.Text, title: _title.Text, content: Rope.Collapse(_content.TextRope)));
        _print = new Button { Text = Loc.GetString("newsroom-ui-print"), HorizontalExpand = true };
        _print.OnPressed += _ => Action?.Invoke(new(NewsroomAction.Print));
        buttons.AddChild(_publish); buttons.AddChild(_print); column.AddChild(buttons);
        var body = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        body.AddChild(column); ContentsContainer.AddChild(body);
    }
    public void UpdateState(NewsroomUiState state)
    {
        if (!_initialized) { _channel.Text = state.Channel; _initialized = true; }
        var needed = Math.Min(5, state.Articles.Count(a => state.Channel == "" || a.Channel == state.Channel));
        _status.SetMessage(FormattedMessage.FromUnformatted(!state.Ready ? Loc.GetString("newsroom-no-power") :
            Loc.GetString("newsroom-ui-status", ("paper", state.Paper), ("need", needed), ("status", state.Status))));
        _photo.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(state.HasPhoto ? "newsroom-photo-attached" : "newsroom-ui-photo-help")));
        _publish.Disabled = !state.Ready || !state.CanPublish;
        _print.Disabled = !state.Ready || !state.CanPrint || needed == 0 || state.Paper < needed;
        var listKey = state.Channel + string.Join(";", state.Articles.Select(a => a.Id.ToString()));
        if (_listKey != listKey)
        {
            _listKey = listKey; CivicUi.Clear(_articles);
            foreach (var article in state.Articles.Where(a => state.Channel == "" || a.Channel == state.Channel))
            {
                var card = CivicUi.Column();
                card.AddChild(CivicUi.Text($"{article.Author} | {article.Time}\n{article.Channel}"));
                var button = new Button { Text = Loc.GetString("newsroom-ui-read"), HorizontalExpand = true };
                button.OnPressed += _ => Action?.Invoke(new(NewsroomAction.Select, article.Id)); card.AddChild(button);
                _articles.AddChild(CivicUi.Card(article.Title, card));
            }
        }
        if (_selected == state.Selected?.Id && _preview.ChildCount > 0) return;
        _selected = state.Selected?.Id; CivicUi.Clear(_preview);
        _preview.AddChild(state.Selected is { } selected ? CivicUi.Article(selected, true) : CivicUi.Card(Loc.GetString("newsroom-ui-preview"), CivicUi.Text(Loc.GetString("newsroom-ui-select-article"))));
    }
}

public sealed class NewsEditionBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FancyWindow? _window;
    private BoxContainer? _pages;
    protected override void Open()
    {
        base.Open(); _pages = CivicUi.Column();
        _window = new FancyWindow { Title = Loc.GetString("newsroom-edition"), MinSize = new Vector2(420, 400), SetSize = new Vector2(620, 720) };
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false, Margin = new Thickness(12) };
        scroll.AddChild(_pages); _window.ContentsContainer.AddChild(scroll); _window.OnClose += Close;
        Update<NewsEditionUiState>(); _window.OpenCentered();
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state); if (state is not NewsEditionUiState data || _window == null || _pages == null) return;
        _window.Title = data.Title; CivicUi.Clear(_pages);
        foreach (var article in data.Articles) _pages.AddChild(CivicUi.Article(article, true));
    }
    protected override void Dispose(bool disposing)
    { base.Dispose(disposing); if (disposing) { _window?.Dispose(); _window = null; _pages = null; } }
}
