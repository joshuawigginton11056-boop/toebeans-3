using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Skiing
{
    /// <summary>
    /// Numbers for a tuning pass: speed, the pitch you are on, and how sideways you are. Feel is
    /// judged by hand, but "that felt fast" is a lot easier to act on when you can see it was
    /// 71 km/h on a 22° pitch.
    /// </summary>
    [RequireComponent(typeof(SkiController))]
    [DisallowMultipleComponent]
    public class SkiHud : MonoBehaviour
    {
        [Tooltip("Key that shows and hides the readout.")]
        public Key toggleKey = Key.H;
        public bool visible = true;

        SkiController _ski;
        GUIStyle _style;
        GUIStyle _boxStyle;
        float _topSpeed;

        void Awake() => _ski = GetComponent<SkiController>();

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;

            _topSpeed = Mathf.Max(_topSpeed, _ski.Speed);
        }

        void OnGUI()
        {
            if (!visible)
                return;

            EnsureStyles();

            string state = _ski.IsGrounded
                ? (_ski.RidingSwitch ? "switch" : "on the snow")
                : $"airborne {_ski.AirTime:0.0}s";

            string charge = _ski.JumpCharge01 > 0.01f
                ? $"\ncharge   {new string('|', Mathf.RoundToInt(_ski.JumpCharge01 * 20))}"
                : string.Empty;

            string text =
                $"{_ski.Speed * 3.6f:0} km/h   ({_ski.Speed:0.0} m/s)\n" +
                $"top      {_topSpeed * 3.6f:0} km/h\n" +
                $"pitch    {_ski.SlopeAngle:0}°\n" +
                $"skid     {_ski.SkidAngle:0}°\n" +
                $"{state}{charge}\n\n" +
                "A/D carve · W tuck · S brake\n" +
                "Shift edge · Space jump (hold)\n" +
                "R respawn · H hide · Esc cursor";

            var rect = new Rect(16f, 16f, 300f, 210f);
            GUI.Box(rect, GUIContent.none, _boxStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 20f, rect.height - 16f), text, _style);
        }

        void EnsureStyles()
        {
            if (_style != null)
                return;

            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                richText = false,
                alignment = TextAnchor.UpperLeft,
            };
            _style.normal.textColor = Color.white;

            var background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.45f));
            background.Apply();
            _boxStyle = new GUIStyle { normal = { background = background } };
        }
    }
}
