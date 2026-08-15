using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Karting
{
    /// <summary>
    /// On-screen readout for judging the map from the driving seat: how fast you are actually going,
    /// what you are driving on, and how much grip that surface is giving you. The surface line is the
    /// useful one — it turns "this bit feels wrong" into a specific layer you can go and repaint.
    /// </summary>
    [DisallowMultipleComponent]
    public class KartHud : MonoBehaviour
    {
        [Tooltip("Key that shows/hides the readout.")]
        public Key toggleKey = Key.H;
        public bool visibleOnStart = true;

        KartController _kart;
        GUIStyle _panelStyle;
        GUIStyle _labelStyle;
        Texture2D _panelTexture;
        bool _visible;

        void Awake()
        {
            _kart = GetComponent<KartController>() ?? FindAnyObjectByType<KartController>();
            _visible = visibleOnStart;
        }

        void OnDestroy()
        {
            if (_panelTexture != null)
                Destroy(_panelTexture);
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                _visible = !_visible;
        }

        void OnGUI()
        {
            if (!_visible || _kart == null)
                return;

            EnsureStyles();

            KartSurface surface = _kart.CurrentSurface;
            Vector3 position = _kart.transform.position;

            var text = new System.Text.StringBuilder();
            text.AppendLine("<b>KART</b>");
            text.AppendLine($"Speed              {_kart.SpeedKph:0} km/h   ({_kart.ForwardSpeed:0.0} m/s)");
            text.AppendLine($"Surface            {surface.name}");
            text.AppendLine($"Grip               {surface.forwardGrip:0.00} fwd  ·  {surface.sidewaysGrip:0.00} side");
            text.AppendLine($"Rolling drag       {surface.rollingResistance:0.000}");
            text.AppendLine($"Wheels on ground   {_kart.GroundedWheels} of 4{(_kart.IsAirborne ? "   — airborne" : "")}");
            text.AppendLine($"Position           {position.x:0.0}, {position.y:0.0}, {position.z:0.0}");
            text.AppendLine($"Mass               {_kart.TotalMass:0} kg");
            text.AppendLine($"Engine             {_kart.EngineRpm:0} rpm   (slip {_kart.DriveSlip:0.00})");
            text.AppendLine();
            text.AppendLine("<b>INPUT</b>");
            text.AppendLine($"Throttle           {Bar(_kart.ThrottleInput)}  {_kart.ThrottleInput:0.00}");
            text.AppendLine($"Brake / reverse    {Bar(_kart.ReverseInput)}  {_kart.ReverseInput:0.00}");
            text.AppendLine($"Steer              {Bar(Mathf.Abs(_kart.SteerInput))}  {_kart.SteerInput:+0.00;-0.00; 0.00}");
            text.AppendLine($"Handbrake          {(_kart.HandbrakeInput ? "on" : "off")}");

            // The single most confusing failure is a kart that will not move because the keyboard is
            // going to a different Editor panel. Say so, rather than leaving it looking like physics.
            bool anyInput = _kart.ThrottleInput > 0.01f || _kart.ReverseInput > 0.01f
                            || Mathf.Abs(_kart.SteerInput) > 0.01f || _kart.HandbrakeInput;
            if (!anyInput)
            {
                text.AppendLine(Keyboard.current == null
                    ? "  ! No keyboard detected."
                    : "  ! No input arriving — click the Game view to give it keyboard focus.");
            }

            text.AppendLine();
            text.AppendLine("<b>SCALE</b>");
            text.AppendLine("Driver 1.80 m standing · kart 2.4 m long, 1.34 m wide");
            text.AppendLine();
            string source = _kart.Input != null ? _kart.Input.SourceDescription : "none";
            text.AppendLine($"Input              [{source}]");
            text.Append("W/S throttle & brake · A/D steer · Space handbrake\n" +
                        "R recover · C look back · Mouse orbit · H hide this");

            string body = text.ToString();
            Vector2 size = _labelStyle.CalcSize(new GUIContent(body));
            var rect = new Rect(12f, 12f, Mathf.Max(380f, size.x + 24f), size.y + 20f);

            GUI.Box(rect, GUIContent.none, _panelStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, rect.height - 20f),
                body, _labelStyle);
        }

        /// <summary>A ten-cell meter, so a glance tells you whether input is arriving at all.</summary>
        static string Bar(float value01)
        {
            int filled = Mathf.RoundToInt(Mathf.Clamp01(value01) * 10f);
            return "[" + new string('|', filled) + new string('.', 10 - filled) + "]";
        }

        void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _panelTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            _panelTexture.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _panelTexture;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = false,
                alignment = TextAnchor.UpperLeft,
                fontSize = 13
            };
            _labelStyle.normal.textColor = Color.white;
        }
    }
}
