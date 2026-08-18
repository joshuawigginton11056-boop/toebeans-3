using UnityEditor;
using UnityEngine;

namespace Volcano.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="MistShelter"/>, plus the menu item that puts one on a bridge.
    ///
    /// The report is the point of it. A shelter is invisible until fog reaches it, so the two
    /// questions worth answering without pressing play are whether it baked at all and how much
    /// headroom it left, and both of those are numbers rather than a picture.
    /// </summary>
    [CustomEditor(typeof(MistShelter))]
    [CanEditMultipleObjects]
    public class MistShelterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var shelter = target as MistShelter;
            if (shelter == null) return;

            EditorGUILayout.Space();

            if (GUILayout.Button("Rebake"))
            {
                foreach (Object t in targets)
                {
                    var s = t as MistShelter;
                    if (s != null) s.Rebake();
                }
                SceneView.RepaintAll();
            }

            if (!shelter.Ready)
            {
                EditorGUILayout.HelpBox(
                    "Nothing baked. This wants a Mesh Filter on the object, or one dropped into " +
                    "Surface, and a submesh with triangles in it.", MessageType.Warning);
                return;
            }

            Bounds b = shelter.Bounds;
            EditorGUILayout.HelpBox(
                string.Format(
                    "Lid over {0:0} x {1:0} m, {2} cells at {3:0.##} m.\n" +
                    "Fog is held below {4:0.0} m and let go by {5:0.0} m out.",
                    b.size.x, b.size.z, shelter.CellCount, shelter.BakedCellSize,
                    b.max.y, shelter.margin),
                MessageType.None);

            if (shelter.clearance < 1f)
            {
                EditorGUILayout.HelpBox(
                    "Clearance is under a metre. The lid is measured down from the top of the " +
                    "deck, so it has to clear the thickness of the slab, or fog is held inside " +
                    "the road rather than underneath it.", MessageType.Warning);
            }
        }

        // ------------------------------------------------------------------ menu

        /// <summary>
        /// Deliberately not a button on the bridge's own inspector. Rock Bridge knows nothing about
        /// fog and there is no reason it should: a shelter is a mesh and a lid, and the same
        /// command works on a viaduct, a tunnel mouth or a stretch of track.
        /// </summary>
        [MenuItem("GameObject/Effects/Keep Fog Under This", false, 12)]
        public static void AddToSelection()
        {
            int added = 0;

            foreach (GameObject go in Selection.gameObjects)
            {
                if (go.GetComponent<MeshFilter>() == null) continue;
                if (go.GetComponent<MistShelter>() != null) continue;

                Undo.AddComponent<MistShelter>(go);
                added++;
            }

            if (added == 0)
                Debug.LogWarning("Keep Fog Under This: nothing selected that has a mesh and does " +
                                 "not already have a Mist Shelter on it.");
        }

        [MenuItem("GameObject/Effects/Keep Fog Under This", true)]
        public static bool CanAddToSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
                if (go.GetComponent<MeshFilter>() != null) return true;

            return false;
        }
    }
}
