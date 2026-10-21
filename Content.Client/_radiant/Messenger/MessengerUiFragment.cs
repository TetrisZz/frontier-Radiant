using System.Linq;
using Content.Shared._radiant.Messenger;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using Robust.Shared.Timing;

namespace Content.Client._radiant.Messenger;

public sealed class MessengerUiFragment : BoxContainer
{
    public event Action<MessengerUiMessage>? OnMessage;
    private readonly BoxContainer _contacts = new() { Name = "MessengerContacts", HorizontalExpand = true, Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly BoxContainer _messages = new() { Name = "MessengerMessages", HorizontalExpand = true, Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly LineEdit _search = new() { PlaceHolder = Loc.GetString("messenger-search") };
    private readonly LineEdit _input = new() { Name = "MessengerInput", PlaceHolder = Loc.GetString("messenger-input"), HorizontalExpand = true };
    private readonly Button _dialogsTab = new MessengerButton { Name = "MessengerDialogsTab", ToggleMode = true, HorizontalExpand = true, MinWidth = 98, SetHeight = 32, Text = Loc.GetString("messenger-tab-dialogs") };
    private readonly Button _contactsTab = new MessengerButton { Name = "MessengerContactsTab", ToggleMode = true, Pressed = true, HorizontalExpand = true, MinWidth = 98, SetHeight = 32, Text = Loc.GetString("messenger-tab-contacts") };
    private readonly Label _online = new() { ClipText = true, FontColorOverride = Color.FromHex("#A5B2BC") };
    private readonly Label _length = new() { HorizontalAlignment = HAlignment.Right, FontColorOverride = Color.FromHex("#A5B2BC") };
    private readonly Label _title = new() { ClipText = true, HorizontalExpand = true };
    private readonly Label _status = new() { ClipText = true, FontColorOverride = Color.FromHex("#93ACA5") };
    private readonly Label _error = new() { FontColorOverride = Color.FromHex("#E6A18B"), ClipText = true };
    private readonly CheckBox _mute = new() { Text = Loc.GetString("messenger-notifications"), Pressed = true };
    private readonly TextureButton _send = new MessengerSendButton()
    {
        TexturePath = "/Textures/Interface/Default/filled_right_arrow.svg.192dpi.png",
        Name = "MessengerSend",
        SetSize = new System.Numerics.Vector2(28, 28),
        ToolTip = Loc.GetString("messenger-send"),
    };
    private readonly ScrollContainer _scroll;
    private MessengerUiState? _state;
    private bool _scrollToEnd;
    private bool _showContacts = true;
    private readonly Dictionary<NetEntity, string> _drafts = new();
    private readonly List<(PanelContainer Panel, RichTextLabel Text)> _bubbles = new();

    public MessengerUiFragment()
    {
        HorizontalExpand = VerticalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 0;
        var heading = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8, Margin = new Thickness(8, 6) };
        heading.AddChild(new Label { Text = Loc.GetString("messenger-title"), ClipText = true, HorizontalExpand = true, FontColorOverride = Color.FromHex("#76D6B5") });
        var notificationRow = new BoxContainer { HorizontalExpand = true, Margin = new Thickness(8, 0, 8, 6) };
        notificationRow.AddChild(_mute);
        AddChild(heading);
        AddChild(notificationRow);
        var body = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 1 };
        AddChild(body);
        var sidebar = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        sidebar.AddChild(_online);
        sidebar.AddChild(_search);
        var tabs = new BoxContainer { Name = "MessengerTabs", HorizontalExpand = true, SeparationOverride = 4 };
        var tabGroup = new ButtonGroup();
        _dialogsTab.Group = _contactsTab.Group = tabGroup;
        tabs.AddChild(_dialogsTab);
        tabs.AddChild(_contactsTab);
        sidebar.AddChild(tabs);
        var contactScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        contactScroll.AddChild(_contacts);
        sidebar.AddChild(contactScroll);
        var sidePanel = Panel("#202628", sidebar, 8);
        sidePanel.Name = "MessengerSidebar";
        sidePanel.HorizontalExpand = false;
        sidePanel.RectClipContent = true;
        sidePanel.MinWidth = sidePanel.MaxWidth = 216;
        body.AddChild(sidePanel);

        var conversation = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
        var header = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2, MinHeight = 44 };
        header.AddChild(_title);
        header.AddChild(_status);
        conversation.AddChild(header);
        _scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true };
        _scroll.AddChild(_messages);
        conversation.AddChild(_scroll);
        conversation.AddChild(_error);
        var composer = new BoxContainer { Name = "MessengerComposer", HorizontalExpand = true, SeparationOverride = 6, MinHeight = 28 };
        composer.AddChild(_input);
        composer.AddChild(_send);
        conversation.AddChild(composer);
        conversation.AddChild(_length);
        var conversationPanel = Panel("#171C1E", conversation, 8);
        conversationPanel.Name = "MessengerConversation";
        body.AddChild(conversationPanel);
        _input.Editable = false;
        UpdateSend();
        _search.OnTextChanged += _ => RenderContacts();
        _dialogsTab.OnToggled += args => { if (args.Pressed) SetContactView(false); };
        _contactsTab.OnToggled += args => { if (args.Pressed) SetContactView(true); };
        _input.OnTextEntered += _ => Send();
        _send.OnPressed += _ => Send();
        _mute.OnPressed += _ => OnMessage?.Invoke(new MessengerUiMessage(MessengerAction.ToggleMute));
        _input.OnTextChanged += _ =>
        {
            if (_input.Text.Length > MessengerUiMessage.MaxLength)
                _input.Text = _input.Text[..MessengerUiMessage.MaxLength];
            UpdateSend();
        };
    }

    private void SetContactView(bool contacts)
    {
        _showContacts = contacts;
        _contactsTab.Pressed = contacts;
        _dialogsTab.Pressed = !contacts;
        RenderContacts();
    }

    private static PanelContainer Panel(string color, Control content, int padding)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex(color) },
        };
        content.Margin = new Thickness(padding);
        panel.AddChild(content);
        return panel;
    }

    public void UpdateState(MessengerUiState state)
    {
        var contactsChanged = _state == null || _state.Selected != state.Selected || !_state.Contacts.SequenceEqual(state.Contacts);
        var changed = _state?.Selected != state.Selected || _state == null || !_state.Messages.SequenceEqual(state.Messages);
        var followMessages = _state?.Selected != state.Selected ||
            _scroll.VScroll >= _messages.Size.Y - _scroll.Size.Y - 24 ||
            changed && state.Messages.LastOrDefault() is { Incoming: false };
        if (_state?.Selected != state.Selected)
        {
            if (_state?.Selected is { } previous)
            {
                if (string.IsNullOrEmpty(_input.Text))
                    _drafts.Remove(previous);
                else
                    _drafts[previous] = _input.Text;
            }
            _input.Text = state.Selected is { } next ? _drafts.GetValueOrDefault(next, "") : "";
        }
        else if (changed && state.Messages.LastOrDefault() is { Incoming: false } last && last.Text == _input.Text.Trim())
        {
            _input.Clear();
            if (state.Selected is { } delivered)
                _drafts.Remove(delivered);
        }
        _state = state;
        _online.Text = Loc.GetString("messenger-online-count", ("count", state.Contacts.Count(c => c.Available)));
        _mute.Pressed = !state.Muted;
        _mute.ToolTip = Loc.GetString(state.Muted ? "messenger-unmute" : "messenger-mute");
        var contact = state.Contacts.FirstOrDefault(c => c.Address == state.Selected);
        _title.Text = contact?.Name ?? Loc.GetString("messenger-select-contact");
        _status.Text = contact == null ? "" : Loc.GetString(contact.Available ? "messenger-available" : "messenger-unavailable");
        _status.Visible = contact != null;
        _title.ToolTip = contact?.Name;
        _input.Editable = contact?.Available == true;
        _error.Text = state.Error == null ? "" : Loc.GetString(state.Error);
        _error.Visible = state.Error != null;
        if (contactsChanged)
            RenderContacts();
        if (changed)
        {
            _messages.DisposeAllChildren();
            _bubbles.Clear();
            if (state.Messages.Count == 0)
            {
                var empty = new RichTextLabel { Margin = new Thickness(8), HorizontalExpand = true };
                empty.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(contact == null ? "messenger-empty" : "messenger-no-messages")));
                _messages.AddChild(empty);
            }
            foreach (var message in state.Messages)
            {
                var row = new BoxContainer { HorizontalExpand = true };
                if (!message.Incoming)
                    row.AddChild(new Control { MinWidth = 20, HorizontalExpand = true });
                var bubble = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 3 };
                var text = new RichTextLabel { MaxWidth = 280 };
                text.SetMessage(FormattedMessage.FromUnformatted(message.Text));
                bubble.AddChild(text);
                bubble.AddChild(new Label
                {
                    Text = $"{(int) message.Time.TotalHours:00}:{message.Time.Minutes:00}",
                    HorizontalAlignment = HAlignment.Right,
                    FontColorOverride = Color.FromHex("#ADC0B8"),
                });
                var messagePanel = Panel(message.Incoming ? "#2C3438" : "#24483F", bubble, 8);
                messagePanel.HorizontalExpand = false;
                messagePanel.MaxWidth = 296;
                row.AddChild(messagePanel);
                _bubbles.Add((messagePanel, text));
                if (message.Incoming)
                    row.AddChild(new Control { MinWidth = 20, HorizontalExpand = true });
                _messages.AddChild(row);
            }
            _scrollToEnd = followMessages;
        }
        UpdateSend();
    }

    private void RenderContacts()
    {
        _contacts.DisposeAllChildren();
        if (_state == null)
            return;
        var search = _search.Text.Trim();
        foreach (var contact in _state.Contacts.Where(c =>
                     (_showContacts ? c.Available : c.Preview.Length > 0 || c.Unread > 0) &&
                     c.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
        {
            var button = new MessengerContactButton { HorizontalExpand = true, Selected = contact.Address == _state.Selected };
            var body = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
            var nameRow = new BoxContainer();
            nameRow.AddChild(new Label { Text = contact.Name, ClipText = true, HorizontalExpand = true, ToolTip = contact.Name });
            if (contact.Unread > 0)
                nameRow.AddChild(new Label { Text = contact.Unread > 99 ? "99+" : contact.Unread.ToString(), FontColorOverride = Color.FromHex("#76D6B5") });
            body.AddChild(nameRow);
            body.AddChild(new Label
            {
                Text = contact.Preview.Length > 0 ? contact.Preview : Loc.GetString(contact.Available ? "messenger-available" : "messenger-unavailable"),
                ClipText = true,
                FontColorOverride = Color.FromHex("#9AA6A7"),
            });
            var row = new BoxContainer { HorizontalExpand = true, SeparationOverride = 0 };
            row.AddChild(new PanelContainer
            {
                SetWidth = 3,
                PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex(contact.Available ? "#76D6B5" : "#68717B") },
            });
            body.Margin = new Thickness(7, 6);
            row.AddChild(body);
            button.AddChild(row);
            button.OnPressed += _ => OnMessage?.Invoke(new MessengerUiMessage(MessengerAction.Select, contact.Address));
            _contacts.AddChild(button);
        }
        if (_contacts.ChildCount == 0)
            _contacts.AddChild(new Label
            {
                Text = Loc.GetString(search.Length > 0 ? "messenger-no-results" : _showContacts ? "messenger-no-contacts" : "messenger-no-dialogs"),
                ClipText = true,
            });
    }

    private void UpdateSend()
    {
        _send.Disabled = !_input.Editable || string.IsNullOrWhiteSpace(_input.Text);
        _length.Text = $"{_input.Text.Length} / {MessengerUiMessage.MaxLength}";
    }

    private void Send()
    {
        if (_send.Disabled || _state?.Selected is not { } selected)
            return;
        OnMessage?.Invoke(new MessengerUiMessage(MessengerAction.Send, selected, _input.Text));
        // Keep the draft until the server confirms delivery.
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var width = Math.Max(48, _scroll.Size.X - 40);
        foreach (var (panel, text) in _bubbles)
        {
            panel.MaxWidth = width + 16;
            text.MaxWidth = width;
        }
        if (!_scrollToEnd)
            return;
        _scroll.VScrollTarget = float.MaxValue;
        _scrollToEnd = false;
    }
}
