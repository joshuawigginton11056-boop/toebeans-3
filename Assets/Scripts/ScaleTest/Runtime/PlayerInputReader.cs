using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.ScaleTest
{
    /// <summary>
    /// Thin wrapper over the project's InputSystem_Actions asset.
    /// If no asset is supplied (or it is missing the expected actions) it falls back to
    /// reading keyboard/mouse/gamepad directly, so the character stays playable regardless.
    /// </summary>
    public class PlayerInputReader
    {
        const string MapName = "Player";

        readonly InputActionMap _map;
        readonly InputAction _move;
        readonly InputAction _look;
        readonly InputAction _jump;
        readonly InputAction _sprint;
        readonly InputAction _crouch;

        public bool UsingActionAsset => _map != null;

        public PlayerInputReader(InputActionAsset asset)
        {
            if (asset == null)
                return;

            _map = asset.FindActionMap(MapName, throwIfNotFound: false);
            if (_map == null)
                return;

            _move = _map.FindAction("Move", throwIfNotFound: false);
            _look = _map.FindAction("Look", throwIfNotFound: false);
            _jump = _map.FindAction("Jump", throwIfNotFound: false);
            _sprint = _map.FindAction("Sprint", throwIfNotFound: false);
            _crouch = _map.FindAction("Crouch", throwIfNotFound: false);

            // Move is the one action we cannot emulate a sensible fallback around, so if it is
            // absent treat the whole asset as unusable and drop back to raw devices.
            if (_move == null)
                _map = null;
        }

        public void Enable() => _map?.Enable();

        public void Disable() => _map?.Disable();

        /// <summary>Normalised movement intent in local screen space (x = strafe, y = forward).</summary>
        public Vector2 Move
        {
            get
            {
                if (_move != null)
                    return Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
                return ReadMoveFallback();
            }
        }

        /// <summary>
        /// Look delta already converted to degrees for this frame. Mouse input is treated as a
        /// per-frame pixel delta, stick input as a per-second rate, which is why the two paths
        /// scale differently.
        /// </summary>
        public Vector2 LookDegrees(float mouseSensitivity, float stickSensitivity, float deltaTime,
            bool allowPointer = true)
        {
            if (_look != null)
            {
                Vector2 raw = _look.ReadValue<Vector2>();
                bool fromPointer = _look.activeControl?.device is Pointer;
                if (fromPointer)
                    return allowPointer ? raw * mouseSensitivity : Vector2.zero;
                return raw * stickSensitivity * deltaTime;
            }

            Vector2 result = Vector2.zero;
            if (allowPointer && Mouse.current != null)
                result += Mouse.current.delta.ReadValue() * mouseSensitivity;
            if (Gamepad.current != null)
                result += Gamepad.current.rightStick.ReadValue() * stickSensitivity * deltaTime;
            return result;
        }

        /// <summary>Short description of where input is coming from, for the on-screen readout.</summary>
        public string SourceDescription => UsingActionAsset ? "InputSystem_Actions" : "raw devices (fallback)";

        public bool JumpPressedThisFrame
        {
            get
            {
                if (_jump != null)
                    return _jump.WasPressedThisFrame();
                return (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                       || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
            }
        }

        /// <summary>
        /// The jump key's level, not its edge. Hold-to-charge jumping needs to know the key is
        /// still down every frame, which WasPressedThisFrame cannot answer.
        /// </summary>
        public bool JumpHeld
        {
            get
            {
                if (_jump != null)
                    return _jump.IsPressed();
                return (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
                       || (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed);
            }
        }

        public bool SprintHeld
        {
            get
            {
                if (_sprint != null)
                    return _sprint.IsPressed();
                return (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed)
                       || (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);
            }
        }

        public bool CrouchPressedThisFrame
        {
            get
            {
                if (_crouch != null)
                    return _crouch.WasPressedThisFrame();
                return (Keyboard.current != null && Keyboard.current.leftCtrlKey.wasPressedThisFrame)
                       || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
            }
        }

        public float ZoomDelta
        {
            get
            {
                float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
                return Mathf.Clamp(scroll / 120f, -1f, 1f);
            }
        }

        static Vector2 ReadMoveFallback()
        {
            Vector2 result = Vector2.zero;

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) result.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) result.x += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) result.y -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) result.y += 1f;
            }

            if (Gamepad.current != null)
                result += Gamepad.current.leftStick.ReadValue();

            return Vector2.ClampMagnitude(result, 1f);
        }
    }
}
