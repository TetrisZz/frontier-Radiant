using Content.Client.Gameplay;
using Content.Client.Construction;
using Content.Client.Hands;
using Content.Client.Interaction;
using Content.Client.Interaction.Panel.Ui;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._radiant;
using Content.Shared.DetailExaminable;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using static Robust.Shared.Input.Binding.PointerInputCmdHandler;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.Interaction;

[UsedImplicitly]
public sealed partial class InteractionUIController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    private InteractionPanelMenu? _interactionWindow;
    private bool _consumePickRelease;
    private bool _showingPickCursor;
    private Texture? _pickCursor;
    private Texture? _previousCursor;
    private float _previousCursorScale;
    private EntityUid? _previousCursorEntity;
    private MenuButton? InteractionButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.InteractionButton;

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenInteractionMenu,
                InputCmdHandler.FromDelegate(_ => ToggleInteractionMenu()))
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(PickTarget, ignoreUp: false, outsidePrediction: true),
                typeof(ActionUIController), typeof(ConstructionSystem), typeof(DragDropSystem))
            .BindBefore(EngineKeyFunctions.UIRightClick, new PointerInputCmdHandler(CancelPicking, outsidePrediction: true),
                typeof(ActionUIController))
            .Register<InteractionUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<InteractionUIController>();
        _interactionWindow?.Close();
        _interactionWindow?.Dispose();
        _interactionWindow = null;
        UpdatePickCursor(false);
        _consumePickRelease = false;
    }

    private bool PickTarget(in PointerInputCmdArgs args)
    {
        if (!_timing.IsFirstTimePredicted)
            return false;
        if (args.State == BoundKeyState.Up)
        {
            var consume = _consumePickRelease;
            _consumePickRelease = false;
            return consume;
        }
        if (_interactionWindow is not { IsOpen: true, PickingTarget: true } window)
            return false;
        _consumePickRelease = true;
        window.TryPickTarget(args.EntityUid);
        UpdatePickCursor(window.PickingTarget);
        return true;
    }

    private bool CancelPicking(in PointerInputCmdArgs args)
    {
        if (!_timing.IsFirstTimePredicted || _interactionWindow is not { IsOpen: true, PickingTarget: true } window)
            return false;
        window.CancelTargetPicking();
        UpdatePickCursor(false);
        return true;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        UpdatePickCursor(_interactionWindow is { IsOpen: true, PickingTarget: true });
    }

    private void UpdatePickCursor(bool picking)
    {
        if (picking == _showingPickCursor || !_overlays.TryGetOverlay<ShowHandItemOverlay>(out var overlay))
            return;
        _showingPickCursor = picking;
        if (picking)
        {
            _pickCursor = _interactionWindow!.TargetPickTexture;
            _previousCursor = overlay.IconOverride;
            _previousCursorScale = overlay.IconOverrideScale;
            _previousCursorEntity = overlay.EntityOverride;
            overlay.IconOverride = _pickCursor;
            overlay.IconOverrideScale = 0.5f;
            overlay.EntityOverride = null;
        }
        else if (overlay.IconOverride == _pickCursor)
        {
            overlay.IconOverride = _previousCursor;
            overlay.IconOverrideScale = _previousCursorScale;
            overlay.EntityOverride = _previousCursorEntity;
        }
    }

    public void LoadButton()
    {
        if (InteractionButton == null)
            return;
        InteractionButton.OnPressed += InteractionButtonOnPressed;
    }

    public void UnloadButton()
    {
        if (InteractionButton == null)
            return;
        InteractionButton.OnPressed -= InteractionButtonOnPressed;
    }

    public void AddConstructor(InteractionPrototype prototype)
    {
        if (_interactionWindow == null)
            return;

        _interactionWindow.HandleAddConstructor(prototype);
    }

    public void AddEditor(InteractionPrototype prototype)
    {
        if (_interactionWindow == null)
            return;

        _interactionWindow.HandleAddEdit(prototype);
    }

    public void DeleteEditor(InteractionPrototype prototype)
    {
        if (_interactionWindow == null)
            return;

        _interactionWindow.HandleDeleteEdit(prototype);
    }

    private void InteractionButtonOnPressed(ButtonEventArgs obj)
    {
        ToggleInteractionMenu();
    }

    private void ToggleInteractionMenu()
    {
        var session = _playerManager.LocalSession;
        if (session?.AttachedEntity is { } user && IsErpDenied(user))
        {
            _interactionWindow?.Close();
            return;
        }

        if (_interactionWindow == null)
        {
            _interactionWindow = UIManager.CreateWindow<InteractionPanelMenu>();
            _interactionWindow.OnClose += OnWindowClosed;
            _interactionWindow.OnOpen += OnWindowOpen;

            if (session?.AttachedEntity.HasValue == true)
            {
                var attached = session.AttachedEntity.Value;
                _interactionWindow.UpdateUser(attached);
            }
            _interactionWindow.OpenCenteredRight();
        }
        else if (_interactionWindow.IsOpen)
        {
            _interactionWindow.Close();
        }
        else
        {
            if (session?.AttachedEntity is { } attached)
                _interactionWindow.UpdateUser(attached);
            _interactionWindow.OpenCenteredRight();
        }
    }

    private void OnWindowClosed()
    {
        if (InteractionButton != null)
            InteractionButton.Pressed = false;

        UpdatePickCursor(false);
    }

    private void OnWindowOpen()
    {
        if (InteractionButton != null)
            InteractionButton.Pressed = true;
    }

    private bool IsErpDenied(EntityUid uid)
    {
        return _entManager.TryGetComponent<DetailExaminableComponent>(uid, out var detail) &&
               detail.ERPStatus == EnumERPStatus.NO;
    }
}
