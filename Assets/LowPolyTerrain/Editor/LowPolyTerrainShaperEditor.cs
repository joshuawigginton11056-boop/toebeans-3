using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LowPolyTerrain.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="LowPolyTerrainShaper"/>. Everything here exists so you can see what
    /// a setting will do before it overwrites the heightmap: the stats are computed from a dry-run
    /// build, and the two numbers that actually matter - steepest driveable slope and how much
    /// playable ground is left - are called out as warnings when they go wrong.
    /// </summary>
    [CustomEditor(typeof(LowPolyTerrainShaper))]
    public class LowPolyTerrainShaperEditor : UnityEditor.Editor
    {
        LowPolyTerrainBuilder.Result _preview;
        bool _hasPreview;
        int _protectedCount;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var shaper = (LowPolyTerrainShaper)target;
            Terrain terrain = shaper.Terrain;

            if (terrain == null || terrain.terrainData == null)
            {
                EditorGUILayout.HelpBox("No Terrain / TerrainData on this GameObject.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Refresh Stats (dry run)"))
                RefreshPreview(shaper, terrain);

            if (_hasPreview)
                DrawStats(shaper, terrain, _preview);

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.7f, 0.9f, 0.7f);
                if (GUILayout.Button("Shape Terrain", GUILayout.Height(28)))
                {
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Shape Low Poly Terrain");
                    _preview = shaper.Apply();
                    _hasPreview = true;
                    EditorUtility.SetDirty(shaper);
                    EditorUtility.SetDirty(terrain.terrainData);
                    AssetDatabase.Refresh();
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("Randomise Seed", GUILayout.Height(28)))
                {
                    Undo.RecordObject(shaper, "Randomise Low Poly Terrain");
                    shaper.Settings.seed = Random.Range(int.MinValue, int.MaxValue);
                    EditorUtility.SetDirty(shaper);
                    RefreshPreview(shaper, terrain);
                }
            }

            using (new EditorGUI.DisabledScope(!shaper.Settings.paintLayers))
            {
                if (GUILayout.Button("Repaint Layers Only"))
                {
                    TerrainData data = terrain.terrainData;
                    Undo.RegisterCompleteObjectUndo(data, "Repaint Low Poly Terrain");
                    int res = data.heightmapResolution;
                    if (shaper.Paint(data, data.GetHeights(0, 0, res, res)))
                        EditorUtility.SetDirty(data);
                }
            }

            using (new EditorGUI.DisabledScope(!shaper.HasBackup))
            {
                if (GUILayout.Button("Restore Original Heightmap"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Restore original heightmap?",
                            "This puts the terrain back exactly as it was before the first shape, " +
                            "discarding the generated world.",
                            "Restore", "Cancel"))
                    {
                        Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Restore Terrain");
                        if (shaper.Restore())
                            EditorUtility.SetDirty(terrain.terrainData);
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                shaper.HasBackup
                    ? "Original heightmap is backed up at:\n" + shaper.BackupPath
                    : "No backup yet. The original heightmap is saved automatically the first time " +
                      "you press Shape Terrain, and Restore brings it back at any point.",
                shaper.HasBackup ? MessageType.None : MessageType.Info);

            EditorGUILayout.HelpBox(
                "Facets come from the height field being planar over each lattice triangle, not from " +
                "the vertex count - Unity still draws the terrain at its own resolution and LOD. " +
                "This changes how the ground reads, not how much it costs.",
                MessageType.None);
        }

        void RefreshPreview(LowPolyTerrainShaper shaper, Terrain terrain)
        {
            _preview = shaper.Preview();
            _hasPreview = true;

            int res = terrain.terrainData.heightmapResolution;
            List<ProtectedArea> areas = shaper.CollectProtectedAreas(
                terrain, terrain.terrainData.GetHeights(0, 0, res, res), res);
            _protectedCount = areas.Count;
        }

        void DrawStats(LowPolyTerrainShaper shaper, Terrain terrain, LowPolyTerrainBuilder.Result r)
        {
            TerrainData data = terrain.terrainData;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Facet grid",
                string.Format("{0} x {1}  ({2:F1} x {3:F1} m, {4:N0} facet triangles)",
                    r.FacetCellsX, r.FacetCellsZ, r.ActualFacetSizeX, r.ActualFacetSizeZ, r.TriangleCount));

            EditorGUILayout.LabelField("Height range",
                string.Format("{0:F1} m to {1:F1} m  (terrain ceiling {2:F0} m)",
                    r.MinHeight, r.MaxHeight, data.size.y));

            EditorGUILayout.LabelField("Steepest on open ground",
                string.Format("{0:F1} deg", r.MaxPanSlopeDegrees));

            EditorGUILayout.LabelField("Steepest anywhere",
                string.Format("{0:F1} deg", r.MaxSlopeDegrees));

            EditorGUILayout.LabelField("Playable span",
                string.Format("{0:F0} x {0:F0} m inside the wall foot", r.PlayableSpan));

            EditorGUILayout.LabelField("Protected areas", _protectedCount.ToString());

            if (r.ClampedAtCeiling)
            {
                EditorGUILayout.HelpBox(
                    string.Format(
                        "The wall wants to go above the terrain's own {0:F0} m height ceiling and is " +
                        "being flattened off. Either lower Wall Height, or raise the terrain's Height " +
                        "in Terrain Settings.", data.size.y),
                    MessageType.Warning);
            }

            if (r.MaxPanSlopeDegrees > 25f)
            {
                EditorGUILayout.HelpBox(
                    string.Format(
                        "Open ground reaches {0:F0} deg, which is steep for a kart. Lower Pan Relief " +
                        "or raise Pan Wavelength.", r.MaxPanSlopeDegrees),
                    MessageType.Warning);
            }

            if (shaper.Settings.buildWall && r.PlayableSpan < 60f)
            {
                EditorGUILayout.HelpBox(
                    string.Format(
                        "Only {0:F0} m of open ground is left. Reduce Wall Width or Foot Wander if you " +
                        "need room for a track.", r.PlayableSpan),
                    MessageType.Warning);
            }
        }
    }

    /// <summary>Puts the shaper on the selected terrain without hunting through Add Component.</summary>
    public static class LowPolyTerrainMenu
    {
        [MenuItem("GameObject/3D Object/Low Poly Terrain Shaper", false, 13)]
        public static void AddToSelection()
        {
            Terrain terrain = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Terrain>()
                : null;

            if (terrain == null)
                terrain = Object.FindAnyObjectByType<Terrain>();

            if (terrain == null)
            {
                EditorUtility.DisplayDialog(
                    "No terrain", "Select a GameObject with a Terrain component first.", "OK");
                return;
            }

            if (terrain.GetComponent<LowPolyTerrainShaper>() == null)
                Undo.AddComponent<LowPolyTerrainShaper>(terrain.gameObject);

            Selection.activeGameObject = terrain.gameObject;
        }
    }
}
