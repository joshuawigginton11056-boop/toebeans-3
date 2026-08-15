using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.ScaleTest
{
    /// <summary>
    /// On-screen readout for judging world scale: the character's own dimensions, how fast it is
    /// actually travelling, and the measured size of whatever is under the crosshair.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScaleHud : MonoBehaviour
    {
        [Tooltip("Key that shows/hides the readout.")]
        public Key toggleKey = Key.H;
        [Tooltip("How far the measuring ray reaches, in metres.")]
        public float measureDistance = 500f;
        public LayerMask measureLayers = ~0;
        public bool visibleOnStart = true;

        ThirdPersonController _controller;
        Camera _camera;
        GUIStyle _panelStyle;
        GUIStyle _labelStyle;
        Texture2D _panelTexture;
        bool _visible;

        // Cached so the readout does not flicker between frames where the ray misses.
        string _measurement = "—";

        void Awake()
        {
            _controller = GetComponent<ThirdPersonController>();
            if (_controller == null)
                _controller = FindAnyObjectByType<ThirdPersonController>();
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

            if (!_visible)
                return;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera != null)
                _measurement = Measure(_camera);
        }

        string Measure(Camera cam)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, measureDistance, measureLayers, QueryTriggerInteraction.Ignore))
                return "—";

            if (_controller != null && hit.collider.transform.IsChildOf(_controller.transform))
                return "—";

            string name = hit.collider.name;
            float distance = hit.distance;

            if (hit.collider is TerrainCollider)
                return $"{name}  ·  {distance:0.0} m away  ·  ground";

            Bounds bounds = hit.collider.bounds;
            Renderer renderer = hit.collider.GetComponent<Renderer>();
            if (renderer != null)
                bounds = renderer.bounds;

            Vector3 size = bounds.size;
            return $"{name}\n  {distance:0.0} m away  ·  {size.x:0.00} × {size.y:0.00} × {size.z:0.00} m (w×h×d)";
        }

        void OnGUI()
        {
            if (!_visible || _controller == null)
                return;

            EnsureStyles();

            float speed = _controller.PlanarSpeed;
            var text = new System.Text.StringBuilder();
            text.AppendLine("<b>SCALE TEST</b>");
            text.AppendLine($"Character height   {_controller.CurrentHeight:0.00} m" +
                            (_controller.IsCrouching ? "  (crouched)" : ""));
            text.AppendLine($"Eye height         {_controller.EyeHeight:0.00} m");
            text.AppendLine($"Speed              {speed:0.0} m/s   ({speed * 3.6f:0.0} km/h)");
            Vector3 position = _controller.transform.position;
            text.AppendLine($"Position           {position.x:0.0}, {position.y:0.0}, {position.z:0.0}");
            text.AppendLine($"Grounded           {(_controller.IsGrounded ? "yes" : "no")}");
            Vector2 move = _controller.MoveInput;
            string source = _controller.Input != null ? _controller.Input.SourceDescription : "none";
            text.AppendLine($"Input              {move.x:0.00}, {move.y:0.00}   [{source}]");
            text.AppendLine($"Cursor             {(Cursor.lockState == CursorLockMode.Locked ? "captured" : "free — click the Game view")}");
            text.AppendLine();
            text.AppendLine("<b>LOOKING AT</b>");
            text.AppendLine(_measurement);
            text.AppendLine();
            text.Append("WASD move · Shift sprint · Space jump · Ctrl crouch\n" +
                        "V first/third person · H hide this · Esc free cursor");

            string body = text.ToString();
            Vector2 size = _labelStyle.CalcSize(new GUIContent(body));
            var rect = new Rect(12f, 12f, Mathf.Max(340f, size.x + 24f), size.y + 20f);

            GUI.Box(rect, GUIContent.none, _panelStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, rect.height - 20f), body, _labelStyle);

            DrawCrosshair();
        }

        void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(cx - 6f, cy - 1f, 12f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 6f, 2f, 12f), Texture2D.whiteTexture);
            GUI.color = Color.white;
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
