using Content.Shared._radiant.Supporters;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Supporters;

public sealed partial class SupporterSystem : SharedSupporterSystem
{
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IClipboardManager _clipboard = default!;
    private bool _active;
    private DefaultWindow? _linkWindow;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SupporterStatusEvent>(message => _active = message.Active);
        SubscribeNetworkEvent<SupporterLinkCodeEvent>(ShowLinkCode);
        _players.PlayerStatusChanged += OnSessionStatus;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnSessionStatus;
        _linkWindow?.Dispose();
        _linkWindow = null;
        base.Shutdown();
    }

    private void OnSessionStatus(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected) return;
        _active = false;
        _linkWindow?.Dispose();
        _linkWindow = null;
    }

    private void ShowLinkCode(SupporterLinkCodeEvent message)
    {
        _linkWindow?.Dispose();
        var window = new DefaultWindow
        {
            Title = Loc.GetString("supporter-link-window-title"),
            MinWidth = 560,
            SetSize = new System.Numerics.Vector2(620, 200),
        };
        _linkWindow = window;
        var contents = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10,
        };
        var instructions = new RichTextLabel();
        instructions.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("supporter-link-window-instructions")));
        contents.AddChild(instructions);
        contents.AddChild(new LineEdit { Text = message.Command, Editable = false, HorizontalExpand = true });
        var copy = new Button { Text = Loc.GetString("supporter-link-copy") };
        copy.OnPressed += _ =>
        {
            _clipboard.SetText(message.Command);
            copy.Text = Loc.GetString("supporter-link-copied");
        };
        contents.AddChild(copy);
        var expiry = new RichTextLabel();
        expiry.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("supporter-link-window-expiry")));
        contents.AddChild(expiry);
        window.Contents.AddChild(contents);
        window.OnClose += () =>
        {
            if (_linkWindow == window) _linkWindow = null;
            window.Dispose();
        };
        window.OpenCentered();
    }

    public override bool HasAccess(NetUserId userId)
        => _players.LocalSession?.UserId == userId && _active;
}
