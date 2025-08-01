using Content.Client.Gameplay;
using Content.Client.Interaction.Panel.Ui;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.Interaction;

[UsedImplicitly]
public sealed class InteractionUIController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private InteractionPanelMenu? _interactionWindow;
    private MenuButton? InteractionButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.InteractionButton;

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenInteractionMenu,
                InputCmdHandler.FromDelegate(_ => ToggleInteractionMenu()))
            .Register<InteractionUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<InteractionUIController>();
        _interactionWindow?.Close();
        _interactionWindow = null;
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
        if (_interactionWindow == null)
        {
            _interactionWindow = UIManager.CreateWindow<InteractionPanelMenu>();
            _interactionWindow.OnClose += OnWindowClosed;
            _interactionWindow.OnOpen += OnWindowOpen;

            var session = _playerManager.LocalSession;
            if (session?.AttachedEntity.HasValue == true)
            {
                var user = session.AttachedEntity.Value;
                _interactionWindow.UpdateUser(user);
            }

            _interactionWindow.OpenCenteredRight();
        }
        else
        {
            _interactionWindow.Close();
        }
    }

    private void OnWindowClosed()
    {
        if (InteractionButton != null)
            InteractionButton.Pressed = false;

        _interactionWindow = null;
    }

    private void OnWindowOpen()
    {
        if (InteractionButton != null)
            InteractionButton.Pressed = true;
    }
}

