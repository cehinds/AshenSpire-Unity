// GamepadDriver.cs — the only code that reads a controller (US-15.3). Legacy Input Manager:
// ProjectSettings activeInputHandler is 0 and the Input System package is not used.
// EACH FRAME: Input.GetKey(JoystickButton0..19) and Input.GetAxisRaw("GamepadAxis1".."GamepadAxis10")
// (axes defined in ProjectSettings/InputManager.asset, raw, every joystick) fill a GamepadFrame
// for GamepadNavigator.Tick. Keyboard, mouse and touch input marks the keyboard as the last device.
// ATTACH: RunController adds this component next to itself and assigns Navigator from its
// CampaignView; clearing Navigator (OnDisable) stops all pad handling.
// LAYOUT: GamepadLayout.ForPlatform(Application.platform) — XInput on desktop, standard on WebGL/Android.
using System;
using AshenSpire.Domain.Original;
using UnityEngine;

namespace AshenSpire.Presentation
{
    public sealed class GamepadDriver : MonoBehaviour
    {
        private static readonly string[] AxisNames =
        {
            "GamepadAxis1", "GamepadAxis2", "GamepadAxis3", "GamepadAxis4", "GamepadAxis5",
            "GamepadAxis6", "GamepadAxis7", "GamepadAxis8", "GamepadAxis9", "GamepadAxis10",
        };
        private readonly GamepadFrame _frame = new GamepadFrame();
        private GamepadNavigator _navigator;
        private bool _axesMissing, _wasFocused = true;
        private Vector3 _mouse;

        public GamepadNavigator Navigator
        {
            get => _navigator;
            set { _navigator = value; if (value != null) value.Layout = GamepadLayout.ForPlatform(PlatformName(UnityEngine.Application.platform)); }
        }

        public static string PlatformName(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.WebGLPlayer: return "webgl";
                case RuntimePlatform.Android: return "android";
                case RuntimePlatform.WindowsPlayer: case RuntimePlatform.WindowsEditor: return "windows";
                case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: return "mac";
                case RuntimePlatform.LinuxPlayer: case RuntimePlatform.LinuxEditor: return "linux";
                default: return platform.ToString().ToLowerInvariant();
            }
        }

        private void Update()
        {
            var navigator = _navigator;
            if (navigator == null) return;
            var focused = UnityEngine.Application.isFocused;
            if (!focused) { if (_wasFocused) navigator.Reset(); _wasFocused = false; return; }
            _wasFocused = true;
            _frame.Clear();
            var anyButton = false;
            for (var i = 0; i < GamepadFrame.ButtonCount; i++)
            {
                _frame.Buttons[i] = Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0 + i));
                anyButton |= _frame.Buttons[i];
            }
            if (!_axesMissing)
            {
                for (var i = 0; i < GamepadFrame.AxisCount; i++)
                {
                    try { _frame.Axes[i] = Input.GetAxisRaw(AxisNames[i]); }
                    catch (ArgumentException)
                    {
                        // An Input Manager without the Gamepad axes: buttons still work, sticks do not.
                        _axesMissing = true; Array.Clear(_frame.Axes, 0, GamepadFrame.AxisCount);
                        Debug.LogWarning("Gamepad axes are missing from ProjectSettings/InputManager.asset; sticks and axis d-pads are ignored.");
                        break;
                    }
                }
            }
            navigator.Tick(_frame, Time.unscaledTimeAsDouble);
            // Last device: a key that is not a joystick button, a mouse click or move, or a touch.
            var mouse = Input.mousePosition;
            var pointerMoved = (mouse - _mouse).sqrMagnitude > 16 && _mouse != Vector3.zero;
            _mouse = mouse;
            if ((Input.anyKeyDown && !anyButton) || pointerMoved || Input.GetMouseButtonDown(0) || Input.touchCount > 0) navigator.NoteKeyboardOrPointer();
        }
    }
}
