using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Toebeans.Game
{
    /// <summary>
    /// One racetrack, as the game knows it outside the track's own scene: an id, how to present it
    /// on the map picker, which scene to load and how long a lap is meant to be.
    ///
    /// Adding a map to the game is making one of these and dropping it in the
    /// <see cref="TrackCatalog"/>. Nothing else needs to change.
    ///
    /// The scene is stored as a path string, with the editor-only <see cref="sceneAsset"/> field
    /// there purely so a human can drag one in. A SceneAsset is a UnityEditor type and does not
    /// exist in a build, so the path is what actually ships - and the path is also what a host
    /// would send a client, since neither end can hand the other an asset reference.
    /// </summary>
    [CreateAssetMenu(menuName = "Toebeans/Track", fileName = "Track_New")]
    public sealed class TrackDefinition : ScriptableObject
    {
        [Tooltip("Stable id, and the only part of this asset that crosses the wire. " +
                 "Renaming the asset is safe; changing this breaks saved results and open lobbies.")]
        public string trackId = string.Empty;

        [Tooltip("Name shown on the map picker and the loading screen.")]
        public string displayName = string.Empty;

        [TextArea(2, 4)]
        public string description = string.Empty;

        [Tooltip("Cup or world this track belongs to. Used to group the map picker.")]
        public string cupId = string.Empty;

        [Header("Presentation")]
        public Sprite preview;

        [Tooltip("Order within the picker. Lower comes first.")]
        public int sortOrder;

        [Tooltip("One to three. Shown as pips on the picker; does not affect the AI yet.")]
        [Range(1, 3)]
        public int difficulty = 1;

#if UNITY_EDITOR
        [Header("Scene")]
        [Tooltip("Drag the track's scene here. Editor only - the path below is what the build uses, " +
                 "and it is kept in sync from this automatically.")]
        public SceneAsset sceneAsset;
#endif

        [Tooltip("Path of the scene to load, e.g. Assets/Scenes/LavaWorld.unity. Filled in from " +
                 "the scene asset above; the scene must also be in Build Settings to load at runtime.")]
        public string scenePath = string.Empty;

        [Header("Rules")]
        [Tooltip("Laps for this track, or 0 to take the mode's default. A long lap wants fewer.")]
        [Min(0)]
        public int defaultLapCount;

        [Tooltip("Most racers this track's grid can physically start. Narrows the mode's limit.")]
        [Min(1)]
        public int maxRacers = 8;

        [Tooltip("Off hides the track from the picker without deleting it - for maps that are " +
                 "still being built.")]
        public bool isPlayable = true;

        /// <summary>Scene name without the path or extension, which is what SceneManager wants.</summary>
        public string SceneName
        {
            get
            {
                if (string.IsNullOrEmpty(scenePath))
                    return string.Empty;

                int slash = scenePath.LastIndexOf('/');
                int start = slash >= 0 ? slash + 1 : 0;
                int dot = scenePath.LastIndexOf('.');
                int end = dot > start ? dot : scenePath.Length;
                return scenePath.Substring(start, end - start);
            }
        }

        /// <summary>Set up enough to actually be raced on.</summary>
        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(trackId) && !string.IsNullOrEmpty(scenePath); }
        }

        /// <summary>Offered to players right now.</summary>
        public bool IsSelectable { get { return isPlayable && IsValid; } }

        void OnValidate()
        {
            if (string.IsNullOrEmpty(trackId))
                trackId = name;
            if (string.IsNullOrEmpty(displayName))
                displayName = name;

#if UNITY_EDITOR
            // The path is the field that ships, so it is derived from the drag-and-drop field
            // rather than typed. Clearing the asset deliberately does not clear the path: a track
            // whose scene has been moved should keep pointing somewhere until a human fixes it,
            // instead of quietly emptying itself.
            if (sceneAsset != null)
            {
                string path = AssetDatabase.GetAssetPath(sceneAsset);
                if (!string.IsNullOrEmpty(path) && path != scenePath)
                    scenePath = path;
            }
#endif
        }
    }
}
