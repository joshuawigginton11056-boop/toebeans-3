using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Karting
{
    /// <summary>
    /// Reads driving intent from the project's InputSystem_Actions asset, falling back to raw devices
    /// when the asset is missing an action — the same arrangement the on-foot controller uses.
    ///
    /// Gamepad triggers are read directly rather than through the asset: the Player map has no analogue
    /// trigger actions, and an on/off throttle is the fastest way to make a kart feel like a toy.
    /// </summary>
    public class KartInputReader
    {
        const string MapName = "Player";

        readonly InputActionMap _map;
        readonly InputAction _move;
        readonly InputAction _look;
        readonly InputAction _handbrake;

        public bool UsingActionAsset => _map != null;

        public string SourceDescription => UsingActionAsset ? "InputSystem_Actions" : "raw devices (fallback)";

        public KartInputReader(InputActionAsset asset)
        {
            if (asset == null)
                return;

            _map = asset.FindActionMap(MapName, throwIfNotFound: false);
            if (_map == null)
                return;

            _move = _map.FindAction("Move", throwIfNotFound: false);
            _look = _map.FindAction("Look", throwIfNotFound: false);
            _handbrake = _map.FindAction("Jump", throwIfNotFound: false);

            if (_move == null)
                _map = null;
        }

        public void Enable() => _map?.Enable();

        public void Disable() => _map?.Disable();

        /// <summary>-1 full left to +1 full right.</summary>
        public float Steer => Mathf.Clamp(RawMove().x, -1f, 1f);

        /// <summary>
        /// 0 to 1. Prefers the right trigger when a pad is connected, so throttle is analogue.
        /// </summary>
        public float Throttle
        {
            get
            {
                Gamepad pad = Gamepad.current;
                if (pad != null)
                {
                    float trigger = pad.rightTrigger.ReadValue();
                    if (trigger > 0.01f)
                        return trigger;
                }
                return Mathf.Clamp01(RawMove().y);
            }
        }

        /// <summary>0 to 1. Brake on the way forward, reverse once stopped — the controller decides which.</summary>
        public float Reverse
        {
            get
            {
                Gamepad pad = Gamepad.current;
                if (pad != null)
                {
                    float trigger = pad.leftTrigger.ReadValue();
                    if (trigger > 0.01f)
                        return trigger;
                }
                return Mathf.Clamp01(-RawMove().y);
            }
        }

        public bool HandbrakeHeld
        {
            get
            {
                if (_handbrake != null)
                    return _handbrake.IsPressed();
                return (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
                       || (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed);
            }
        }

        public bool ResetPressedThisFrame =>
            (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);

        /// <summary>
        /// Camera look, already in degrees for this frame. Mouse is a per-frame pixel delta, stick is a
        /// per-second rate, which is why they scale differently.
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

        public float ZoomDelta
        {
            get
            {
                float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
                return Mathf.Clamp(scroll / 120f, -1f, 1f);
            }
        }

        Vector2 RawMove()
        {
            if (_move != null)
                return Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);

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
