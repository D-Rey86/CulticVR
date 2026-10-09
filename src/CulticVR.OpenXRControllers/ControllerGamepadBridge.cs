using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using CulticVR.WorldStereoTrial;

namespace CulticVR.OpenXRControllers
{
    public sealed partial class OpenXRControllersPlugin
    {
        private Gamepad? _vrGamepad;
        private OptionsStickRouter _optionsStick;
        private scrMenuControllerV2? _optionsMenuOwner;
        private bool _leftStickWasPressed;
        private bool _suppressLeftStickUntilRelease;
        private bool _suppressRightTriggerUntilRelease;
        private bool _suppressLeftTriggerUntilRelease;
        private float _lastLeftStickPressTime = -100f;
        private const float MenuDoublePressSeconds = 0.4f;
        private static bool _wristQuickMenuCapturing;
        private static GamepadButton? _queuedWristDpadPulse;

        public static void SetWristQuickMenuCapture(bool capture) =>
            _wristQuickMenuCapturing = capture;

        public static void QueueWristQuickMenuDpad(GamepadButton button)
        {
            if (button == GamepadButton.DpadLeft || button == GamepadButton.DpadUp ||
                button == GamepadButton.DpadRight || button == GamepadButton.DpadDown)
                _queuedWristDpadPulse = button;
        }

        private void Update()
        {
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (left.isValid && left.TryGetFeatureValue(XRCommonUsages.isTracked, out bool leftTracked) && !leftTracked)
                left = default;
            if (right.isValid && right.TryGetFeatureValue(XRCommonUsages.isTracked, out bool rightTracked) && !rightTracked)
                right = default;

            if (!left.isValid && !right.isValid)
            {
                RemoveGamepad();
                return;
            }

            if (_vrGamepad == null)
            {
                _vrGamepad = InputSystem.AddDevice<Gamepad>("CULTIC VR Motion Controllers");
            }

            var leftGripPressed = Button(left, XRCommonUsages.gripButton);
            var rightGripPressed = Button(right, XRCommonUsages.gripButton);
            var leftStickPressed = Button(left, XRCommonUsages.primary2DAxisClick);
            var rightStickPressed = Button(right, XRCommonUsages.primary2DAxisClick);
            var leftGripForGame = leftGripPressed;
            var rightGripForGame = rightGripPressed;
            var menuGesture = false;
            if (!leftStickPressed)
                _suppressLeftStickUntilRelease = false;
            if (leftStickPressed && !_leftStickWasPressed)
            {
                if (Time.unscaledTime - _lastLeftStickPressTime <= MenuDoublePressSeconds)
                {
                    menuGesture = true;
                    _suppressLeftStickUntilRelease = true;
                    _lastLeftStickPressTime = -100f;
                }
                else
                {
                    _lastLeftStickPressTime = Time.unscaledTime;
                }
            }
            _leftStickWasPressed = leftStickPressed;

            var rightAxis = Axis(right);
            var game = scrGameControl.Instance;
            var menu = game != null ? game.systemMenu : null;
            var leftRaw = Axis(left);
            // Stick swapping is independent of weapon handedness. It applies
            // only in gameplay; menus retain the physical left stick.
            if (VrSettings.SticksSwapped && game != null && game.gameState == 0)
            { var swap=leftRaw; leftRaw=rightAxis; rightAxis=swap; }
            if (_optionsMenuOwner != menu)
            {
                // The context includes the native menu instance, not just its
                // ID; a new scene/menu cannot inherit a direction latch.
                _optionsMenuOwner = menu;
                _optionsStick.Route(leftRaw, 0, out _);
            }
            var optionsContext = OptionsContext(game, menu);
            var leftAxis = _optionsStick.Route(leftRaw, optionsContext, out var menuDirection);
            var hud = game != null ? game.hudScript : null;
            var wheelOpen = hud != null && (hud.wepWheelState != 0 || hud.invWheelState != 0);
            var wheelButtonHeld = rightGripForGame;
            if (game != null && game.gameState == 0 && hud != null &&
                hud.gameObject.activeInHierarchy && !wheelOpen && !wheelButtonHeld)
            {
                // Gameplay/Look consumes the whole stick. Keep horizontal turn,
                // but leave vertical look to the tracked headset. The weapon wheel
                // reads that same axis, so pass Y through while its right-grip
                // button is held or either radial wheel remains active.
                rightAxis.y = 0f;
            }

            var rightTrigger = 0f;
            var hasRightTrigger = right.isValid && right.TryGetFeatureValue(XRCommonUsages.trigger, out rightTrigger);
            if (!hasRightTrigger)
                rightTrigger = Button(right, XRCommonUsages.primaryButton) ? 1f : 0f;
            if (_wristQuickMenuCapturing && rightTrigger > 0.1f)
                _suppressRightTriggerUntilRelease = true;
            else if (!_wristQuickMenuCapturing && rightTrigger <= 0.1f)
                _suppressRightTriggerUntilRelease = false;

            var state = new GamepadState
            {
                leftStick = leftAxis,
                rightStick = rightAxis,
                leftTrigger = Analog(left, XRCommonUsages.trigger),
                rightTrigger = _wristQuickMenuCapturing || _suppressRightTriggerUntilRelease ? 0f : rightTrigger
            };
            // In left-hand mode the wrist pointer uses LT. Capture that trigger
            // as well as the accepted RT fire suppression while its panel is up.
            if (_wristQuickMenuCapturing && VrSettings.LeftWeapon && state.leftTrigger>.1f)
                _suppressLeftTriggerUntilRelease=true;
            else if (state.leftTrigger<=.1f) _suppressLeftTriggerUntilRelease=false;
            if ((_wristQuickMenuCapturing&&VrSettings.LeftWeapon)||_suppressLeftTriggerUntilRelease) state.leftTrigger=0f;

            // These destinations come from CULTIC's serialized InputActionAsset,
            // observed in the flat sandbox. They retain its native action maps.
            state = state.WithButton(GamepadButton.South,
                hasRightTrigger && Button(right, XRCommonUsages.primaryButton));
            state = state.WithButton(GamepadButton.East, Button(right, XRCommonUsages.secondaryButton));
            state = state.WithButton(GamepadButton.West, Button(left, XRCommonUsages.primaryButton));
            state = state.WithButton(GamepadButton.North, Button(left, XRCommonUsages.secondaryButton));
            state = state.WithButton(GamepadButton.LeftShoulder, leftGripForGame);
            state = state.WithButton(GamepadButton.RightShoulder, rightGripForGame);
            state = state.WithButton(GamepadButton.LeftStick,
                leftStickPressed && !_suppressLeftStickUntilRelease);
            state = state.WithButton(GamepadButton.RightStick, rightStickPressed);
            state = state.WithButton(GamepadButton.Start,
                menuGesture || Button(left, XRCommonUsages.menuButton) || Button(right, XRCommonUsages.menuButton));

            state = state.WithButton(GamepadButton.DpadLeft, menuDirection == MenuDirection.Left);
            state = state.WithButton(GamepadButton.DpadRight, menuDirection == MenuDirection.Right);
            state = state.WithButton(GamepadButton.DpadUp, menuDirection == MenuDirection.Up);
            state = state.WithButton(GamepadButton.DpadDown, menuDirection == MenuDirection.Down);

            if (_queuedWristDpadPulse.HasValue)
                state = state.WithButton(_queuedWristDpadPulse.Value, true);

            // Choosing head/gamepad aim must not remove the input transport
            // needed to get back. Native game/menu state owns this boundary:
            // XR controls can open/navigate menus, never move/fire in gamepad
            // gameplay. A real gamepad keeps its normal native bindings.
            if (!VrSettings.MotionControls && (game == null ||
                (game.gameState != 1 && WorldStereoTrialPlugin.IsGameplayScene(SceneManager.GetActiveScene()))))
                state = new GamepadState().WithButton(GamepadButton.Start,
                    menuGesture || Button(left, XRCommonUsages.menuButton) || Button(right, XRCommonUsages.menuButton));

            InputSystem.QueueStateEvent(_vrGamepad, state);
            _queuedWristDpadPulse = null;
        }

        private void OnDestroy() => RemoveGamepad();

        private void RemoveGamepad()
        {
            _optionsStick = default;
            _optionsMenuOwner = null;
            _leftStickWasPressed = false;
            _suppressLeftStickUntilRelease = false;
            _suppressRightTriggerUntilRelease = false;
            _suppressLeftTriggerUntilRelease = false;
            _wristQuickMenuCapturing = false;
            _queuedWristDpadPulse = null;
            _lastLeftStickPressTime = -100f;
            if (_vrGamepad == null) return;
            InputSystem.RemoveDevice(_vrGamepad);
            _vrGamepad = null;
        }

        private static Vector2 Axis(UnityEngine.XR.InputDevice device) =>
            device.isValid && device.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 value)
                ? value : Vector2.zero;

        private static int OptionsContext(scrGameControl? game, scrMenuControllerV2? menu)
        {
            if (_wristQuickMenuCapturing || game == null || game.gameState != 1 || menu == null ||
                !menu.gameObject.activeInHierarchy || menu.menuState != scrMenuControllerV2.MenuState.menuStateOpen ||
                menu.currentMenu == null) return 0;
            var id = menu.currentMenu.menuID;
            if (IsOptionsMenu(id)) return (int)id + 1;
            // Native dropdown/dialogs are shared by non-options systems. Their
            // current owner must be an options menu, not a stale return value.
            if ((id == scrMenuControllerV2.MenuID.menuDropdown || id == scrMenuControllerV2.MenuID.menuDialog) &&
                menu.dialogPreviousMenu != null && IsOptionsMenu(menu.dialogPreviousMenu.menuID))
                return (int)id + 1;
            return 0;
        }

        private static bool IsOptionsMenu(scrMenuControllerV2.MenuID id)
        {
            if ((int)id == VrSettings.MenuId) return true;
            switch (id)
            {
                case scrMenuControllerV2.MenuID.menuOptions:
                case scrMenuControllerV2.MenuID.menuGameplay:
                case scrMenuControllerV2.MenuID.menuSound:
                case scrMenuControllerV2.MenuID.menuVideo:
                case scrMenuControllerV2.MenuID.menuControls:
                case scrMenuControllerV2.MenuID.menuResolution:
                case scrMenuControllerV2.MenuID.menuConfirmRes:
                case scrMenuControllerV2.MenuID.menuRebindKeyboard:
                case scrMenuControllerV2.MenuID.menuRebindGamepad:
                case scrMenuControllerV2.MenuID.menuGamepad:
                case scrMenuControllerV2.MenuID.menuGlyphs:
                case scrMenuControllerV2.MenuID.menuAimAssist:
                case scrMenuControllerV2.MenuID.menuGyro:
                case scrMenuControllerV2.MenuID.menuAccessibility:
                case scrMenuControllerV2.MenuID.menuQuality:
                case scrMenuControllerV2.MenuID.menuLanguage:
                    return true;
                default: return false;
            }
        }

        private static float Analog(UnityEngine.XR.InputDevice device, InputFeatureUsage<float> feature) =>
            device.isValid && device.TryGetFeatureValue(feature, out float value) ? value : 0f;

        private static bool Button(UnityEngine.XR.InputDevice device, InputFeatureUsage<bool> feature) =>
            device.isValid && device.TryGetFeatureValue(feature, out bool value) && value;
    }
}
