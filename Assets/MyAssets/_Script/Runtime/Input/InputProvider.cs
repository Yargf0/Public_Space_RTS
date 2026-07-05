using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.EventSystems;

public class InputProvider : MonoBehaviour
{
    public static InputProvider Instance { get; private set; }

    private RtsInputActions _actions;

    private InputActionMap _gameplayMap;
    private InputActionMap _buildingMap;

    private InputAction _pointerPositionAction;
    private InputAction _moveAction;
    private InputAction _scrollAction;
    private InputAction _selectAction;
    private InputAction _commandAction;
    private InputAction _attackModifierAction;
    private InputAction _followModifierAction;
    private InputAction _queueModifierAction;
    private InputAction _stopAction;
    private InputAction _fireModeAction;
    private InputAction _groupAction;
    private InputAction _squadronCreateAction;
    private InputAction _squadronDisbandAction;
    private InputAction _placeAction;
    private InputAction _cancelAction;

    public Vector2 PointerPosition { get; private set; }
    public Vector2 MoveInput { get; private set; }
    public Vector2 ScrollDelta { get; private set; }

    public bool IsAttackModifierHeld { get; private set; }
    public bool IsFollowModifierHeld { get; private set; }
    public bool IsQueueModifierHeld { get; private set; }

    public event System.Action OnSelectPressed;
    public event System.Action OnSelectReleased;
    public event System.Action OnCommandPressed;

    public event System.Action OnPlacePressed;
    public event System.Action OnCancelPressed;

    public event System.Action OnStopPressed;
    public event System.Action OnFireModePressed;

    public event System.Action<int> OnGroupSavePressed;
    public event System.Action<int> OnGroupSelectPressed;
    public event System.Action<int> OnGroupAddSelectPressed;

    public event System.Action OnSquadronCreatePressed;
    public event System.Action OnSquadronDisbandPressed;

    public event System.Action<FormationType> OnFormationPressed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _actions = new RtsInputActions();

        CacheMapsAndActions();
        BindCallbacks();
    }

    private void CacheMapsAndActions()
    {
        _gameplayMap = _actions.asset.FindActionMap("Gameplay", false);
        _buildingMap = _actions.asset.FindActionMap("Building", false);

        _pointerPositionAction = FindAction("Gameplay", "PointerPosition");
        _moveAction = FindAction("Gameplay", "Move");
        _scrollAction = FindAction("Gameplay", "Scroll");
        _selectAction = FindAction("Gameplay", "Select");
        _commandAction = FindAction("Gameplay", "Command");
        _attackModifierAction = FindAction("Gameplay", "AttackModifier");
        _followModifierAction = FindAction("Gameplay", "FollowModifier");
        _queueModifierAction = FindAction("Gameplay", "QueueModifier");
        _stopAction = FindAction("Gameplay", "Stop");
        _fireModeAction = FindAction("Gameplay", "FireMode", "CycleFireMode");
        // old GroupSave/GroupSelect used Shift. StrikeGroup hotkeys are manual: Ctrl+1..9 / 1..9 / Shift+1..9
        _groupAction = null;
        _squadronCreateAction = FindAction("Gameplay", "SquadronCreate", "CreateSquadron");
        _squadronDisbandAction = FindAction("Gameplay", "SquadronDisband", "DisbandSquadron");

        _placeAction = FindAction("Building", "Place");
        _cancelAction = FindAction("Building", "Cancel");
    }

    private InputAction FindAction(string mapName, params string[] actionNames)
    {
        if (_actions == null || _actions.asset == null)
            return null;

        foreach (string actionName in actionNames)
        {
            var action = _actions.asset.FindAction($"{mapName}/{actionName}", false);
            if (action != null)
                return action;

            action = _actions.asset.FindAction(actionName, false);
            if (action != null)
                return action;
        }

        return null;
    }

    private void BindCallbacks()
    {
        if (_pointerPositionAction != null)
        {
            _pointerPositionAction.performed += ctx =>
                PointerPosition = ctx.ReadValue<Vector2>();
        }

        if (_moveAction != null)
        {
            _moveAction.performed += ctx =>
                MoveInput = ctx.ReadValue<Vector2>();

            _moveAction.canceled += _ =>
                MoveInput = Vector2.zero;
        }

        if (_selectAction != null)
        {
            _selectAction.started += _ => OnSelectPressed?.Invoke();
            _selectAction.canceled += _ => OnSelectReleased?.Invoke();
        }

        if (_commandAction != null)
        {
            _commandAction.started += _ => OnCommandPressed?.Invoke();
        }

        if (_attackModifierAction != null)
        {
            _attackModifierAction.started += _ => IsAttackModifierHeld = true;
            _attackModifierAction.canceled += _ => IsAttackModifierHeld = false;
        }

        if (_followModifierAction != null)
        {
            _followModifierAction.started += _ => IsFollowModifierHeld = true;
            _followModifierAction.canceled += _ => IsFollowModifierHeld = false;
        }

        if (_queueModifierAction != null)
        {
            _queueModifierAction.started += _ => IsQueueModifierHeld = true;
            _queueModifierAction.canceled += _ => IsQueueModifierHeld = false;
        }

        if (_stopAction != null)
        {
            _stopAction.started += _ => OnStopPressed?.Invoke();
        }

        if (_fireModeAction != null)
        {
            _fireModeAction.started += _ => OnFireModePressed?.Invoke();
        }

        if (_groupAction != null)
        {
            _groupAction.performed += ctx =>
            {
                int number = Mathf.RoundToInt(ctx.ReadValue<float>());
                if (number < 1 || number > 9)
                    return;

                if (IsQueueModifierHeld)
                    OnGroupSavePressed?.Invoke(number);
                else
                    OnGroupSelectPressed?.Invoke(number);
            };
        }

        if (_squadronCreateAction != null)
        {
            _squadronCreateAction.started += _ => OnSquadronCreatePressed?.Invoke();
        }

        if (_squadronDisbandAction != null)
        {
            _squadronDisbandAction.started += _ => OnSquadronDisbandPressed?.Invoke();
        }

        if (_placeAction != null)
        {
            _placeAction.started += _ => OnPlacePressed?.Invoke();
        }

        if (_cancelAction != null)
        {
            _cancelAction.started += _ => OnCancelPressed?.Invoke();
        }
    }

    private void Update()
    {
        if (_scrollAction != null)
            ScrollDelta = _scrollAction.ReadValue<Vector2>();
        else
            ScrollDelta = Vector2.zero;

        HandleStrikeGroupHotkeys();
    }


    private void HandleStrikeGroupHotkeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        if (ctrl && keyboard.gKey.wasPressedThisFrame)
        {
            if (shift)
                OnSquadronDisbandPressed?.Invoke();
            else
                OnSquadronCreatePressed?.Invoke();
            return;
        }

        for (int i = 1; i <= 9; i++)
        {
            Key key = i == 1 ? Key.Digit1 :
                      i == 2 ? Key.Digit2 :
                      i == 3 ? Key.Digit3 :
                      i == 4 ? Key.Digit4 :
                      i == 5 ? Key.Digit5 :
                      i == 6 ? Key.Digit6 :
                      i == 7 ? Key.Digit7 :
                      i == 8 ? Key.Digit8 :
                               Key.Digit9;

            KeyControl digit = keyboard[key];
            if (digit == null || !digit.wasPressedThisFrame)
                continue;

            if (ctrl)
                OnGroupSavePressed?.Invoke(i);
            else if (shift)
                OnGroupAddSelectPressed?.Invoke(i);
            else
                OnGroupSelectPressed?.Invoke(i);

            return;
        }
    }

    private void OnEnable()
    {
        _gameplayMap?.Enable();
        _buildingMap?.Disable();
    }

    private void OnDisable()
    {
        _actions?.Disable();
    }


    public void SwitchToBuilding()
    {
        _gameplayMap?.Disable();
        _buildingMap?.Enable();
    }

    public void SwitchToGameplay()
    {
        _buildingMap?.Disable();
        _gameplayMap?.Enable();
    }


    public void SimulateFormation(FormationType formationType)
    {
        OnFormationPressed?.Invoke(formationType);
    }

    public Vector3 GetWorldPointerPosition()
    {
        if (Camera.main == null)
            return Vector3.zero;

        Vector3 screen = PointerPosition;
        screen.z = Mathf.Abs(Camera.main.transform.position.z);
        return Camera.main.ScreenToWorldPoint(screen);
    }

    public bool IsPointerOverUI()
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject();
    }


    public void SimulateStop()
    {
        OnStopPressed?.Invoke();
    }

    public void SimulateFireMode()
    {
        OnFireModePressed?.Invoke();
    }

    public void SimulateGroupSelect(int groupIndex)
    {
        if (groupIndex < 1 || groupIndex > 9) return;
        OnGroupSelectPressed?.Invoke(groupIndex);
    }

    public void SimulateGroupSave(int groupIndex)
    {
        if (groupIndex < 1 || groupIndex > 9) return;
        OnGroupSavePressed?.Invoke(groupIndex);
    }

    public void SimulateGroupAddSelect(int groupIndex)
    {
        if (groupIndex < 1 || groupIndex > 9) return;
        OnGroupAddSelectPressed?.Invoke(groupIndex);
    }

    public void SimulateSquadronCreate()
    {
        OnSquadronCreatePressed?.Invoke();
    }

    public void SimulateSquadronDisband()
    {
        OnSquadronDisbandPressed?.Invoke();
    }

    public void SimulatePlace()
    {
        OnPlacePressed?.Invoke();
    }

    public void SimulateCancel()
    {
        OnCancelPressed?.Invoke();
    }
}
