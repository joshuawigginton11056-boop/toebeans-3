using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CaveTunnel.EditorTools
{
    /// <summary>
    /// Punches Unity terrain holes wherever the hillside stands inside a cave.
    ///
    /// A cave mesh on its own is not enterable: Unity Terrain is a heightfield that knows nothing
    /// about the tunnel, so near each mouth the ground surface passes straight through the bore and
    /// plugs it. Painting holes there removes both the visible surface and the collider, which is
    /// what turns the mouth into an opening you can drive through.
    ///
    /// Only the mouths end up punched, and that falls out of the test rather than being special
    /// cased: deep inside the hill the surface is far above the crown, so it is not inside the cave
    /// and no hole is wanted.
    ///
    /// Holes live in the TerrainData asset, not the scene, so this writes to
    /// <c>Assets/New Terrain 2.asset</c> and survives a scene revert. Use Clear to take them back out.
    /// </summary>
    public static class CaveTerrainHoles
    {
        /// <summary>
        /// Swells the containment test outwards. Hole texels are just under 2 m here, so a texel
        /// whose centre sits a little outside the bore can still cover ground that intrudes into it.
        /// </summary>
        const float TestPadding = 1.2f;

        public struct Result
        {
            public int Punched;
            public int Tested;
            public bool KeywordEnabled;
            public string Message;
        }

        /// <summary>Punches holes for one cave. Returns what it did, for the caller to report.</summary>
        public static Result Punch(CaveTunnelGenerator gen, Terrain terrain)
        {
            var result = new Result();

            if (gen == null || terrain == null)
            {
                result.Message = "Need both a cave and a terrain.";
                return result;
            }

            CaveVolume volume = gen.Volume;
            if (volume == null)
            {
                gen.Generate();
                volume = gen.Volume;
            }
            if (volume == null)
            {
                result.Message = "The cave has not generated a volume yet.";
                return result;
            }

            TerrainData data = terrain.terrainData;
            int res = data.holesResolution;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;

            Bounds world = TransformBounds(gen.transform, volume.LocalBounds);
            world.Expand(TestPadding * 2f);

            // The whole hole map is read and written in one go, indexed absolutely. Working on a
            // sub-block keyed off the cave's footprint would touch far fewer texels, but it puts a
            // base offset into both the world-position maths and the array indexing, and getting
            // those to agree is exactly the kind of off-by-one that silently punches the wrong
            // ground. At 512x512 the array is a quarter of a megabyte and the loop is skipped by a
            // bounds test before it does any real work, so the saving was never worth the risk.
            //
            // Indexing is [z, x], not [x, y]. Do not trust the array's own dimensions to tell you
            // this — on a square read they are the same number either way, and the parameter order
            // makes the wrong reading look right. It was established by punching one texel at a
            // known index and raycasting the terrain to find where the hole physically landed.
            bool[,] holes = data.GetHoles(0, 0, res, res);

            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res;
                float wx = origin.x + u * size.x;
                if (wx < world.min.x || wx > world.max.x) continue;

                for (int y = 0; y < res; y++)
                {
                    float v = (y + 0.5f) / res;
                    float wz = origin.z + v * size.z;
                    if (wz < world.min.z || wz > world.max.z) continue;

                    result.Tested++;
                    if (!holes[y, x]) continue; // false means "no terrain here" — already punched

                    // The terrain surface directly above this texel. If that surface is standing
                    // inside the cave, it is the thing plugging it.
                    float wy = origin.y + data.GetInterpolatedHeight(u, v);
                    Vector3 local = gen.transform.InverseTransformPoint(new Vector3(wx, wy, wz));
                    if (!volume.Contains(local, TestPadding)) continue;

                    holes[y, x] = false;
                    result.Punched++;
                }
            }

            if (result.Punched == 0)
            {
                result.Message = "Nothing to punch — no terrain is standing inside the cave.";
                return result;
            }

            Undo.RegisterCompleteObjectUndo(data, "Punch Cave Terrain Holes");
            data.SetHoles(0, 0, holes);
            RefreshCollider(terrain);

            result.KeywordEnabled = EnsureHoleKeyword(terrain);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            result.Message = string.Format("Punched {0} hole texels ({1:F0} m2) out of {2} tested.",
                result.Punched,
                result.Punched * (size.x / res) * (size.z / res),
                result.Tested);
            return result;
        }

        /// <summary>Fills every hole back in. The escape hatch when a punch goes wrong.</summary>
        public static void ClearAll(Terrain terrain)
        {
            if (terrain == null) return;

            TerrainData data = terrain.terrainData;
            int res = data.holesResolution;

            var holes = new bool[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    holes[y, x] = true;

            Undo.RegisterCompleteObjectUndo(data, "Clear Terrain Holes");
            data.SetHoles(0, 0, holes);
            RefreshCollider(terrain);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Kicks the physics shape into rereading the hole map. Editing holes leaves the collider on
        /// the old shape, so without this you get a mouth you can see through but not drive through,
        /// which reads as the punch having failed.
        /// </summary>
        static void RefreshCollider(Terrain terrain)
        {
            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider == null) return;

            TerrainData data = collider.terrainData;
            collider.terrainData = null;
            collider.terrainData = data;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// The terrain's material has to actually clip the holes, or they exist for physics only and
        /// the mouth still looks plugged. Returns false if the material cannot do it.
        /// </summary>
        static bool EnsureHoleKeyword(Terrain terrain)
        {
            Material mat = terrain.materialTemplate;
            if (mat == null) return false;
            if (!mat.shader.name.Contains("Holes") && !mat.HasProperty("_TerrainHolesTexture"))
            {
                // Unity's own terrain shaders handle the keyword themselves; ours needs it set.
                if (!mat.shader.name.StartsWith("Nature/Terrain")) return false;
            }

            if (!mat.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                mat.EnableKeyword("_ALPHATEST_ON");
                EditorUtility.SetDirty(mat);
            }
            return true;
        }

        /// <summary>
        /// Swaps the terrain onto a hole-capable material, copying the look off the one it is using.
        /// A new asset rather than a shader swap, because the mountain material is shared with
        /// hundreds of props and changing it in place would hit all of them.
        /// </summary>
        public static Material CreateHoleCapableMaterial(Terrain terrain, string path)
        {
            Shader shader = Shader.Find("Cave/Terrain Flat With Holes");
            if (shader == null)
            {
                Debug.LogError("Shader 'Cave/Terrain Flat With Holes' not found.");
                return null;
            }

            Material source = terrain.materialTemplate;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                // The folder is not guaranteed to exist: the tool ships as scripts and shaders, and
                // CreateAsset into a missing folder fails rather than making one.
                CaveTunnelMenu.EnsureMaterialFolder();
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;

            if (source != null)
            {
                if (source.HasProperty("_Color")) mat.SetColor("_Color", source.GetColor("_Color"));
                if (source.HasProperty("_MainTex")) mat.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                if (source.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", source.GetFloat("_Glossiness"));
                if (source.HasProperty("_Metallic")) mat.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            }

            mat.EnableKeyword("_ALPHATEST_ON");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Bounds TransformBounds(Transform tf, Bounds local)
        {
            Vector3 c = tf.TransformPoint(local.center);
            Vector3 e = local.extents;
            var result = new Bounds(c, Vector3.zero);

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(tf.TransformPoint(local.center + corner));
            }
            return result;
        }

        /// <summary>
        /// Finds colliders standing inside the bore that terrain holes cannot help with — scenery
        /// meshes the cave has been routed through. A prop shell blocks the passage exactly like the
        /// terrain did, but there is no hole map for it, so the only fixes are scene-side: move the
        /// cave, move the prop, or drop the prop's collider. Reported rather than fixed, because
        /// which of those is right is a level decision.
        ///
        /// Returns collider name -> how many probes down the bore it blocked.
        /// </summary>
        public static Dictionary<string, int> FindBoreObstructions(CaveTunnelGenerator gen, int probes = 60)
        {
            var found = new Dictionary<string, int>();
            if (gen == null || gen.Volume == null || gen.Nodes.Count < 2) return found;

            // Line casts, not overlap spheres. The things that block a cave here are POLY_Mountain
            // shells — hollow surfaces, not solids — and a probe sphere floating in the empty middle
            // of one touches no triangle at all, so an overlap test reports the passage clear while
            // you drive into a wall. A line from one end to the other crosses the shell surface and
            // cannot miss it. It is also the question actually being asked: can something get through?
            List<CaveNode> nodes = gen.Nodes;

            var lane = new[] { -0.45f, 0f, 0.45f };  // fractions of the local half-width
            Vector3 previous = Vector3.zero;
            bool hasPrevious = false;

            for (int p = 0; p <= probes; p++)
            {
                float t = (float)p / probes * (nodes.Count - 1);
                int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, nodes.Count - 2);
                float f = t - i;

                Vector3 local = Vector3.Lerp(nodes[i].position, nodes[i + 1].position, f);
                float width = Mathf.Lerp(nodes[i].width, nodes[i + 1].width, f);
                float height = Mathf.Lerp(nodes[i].height, nodes[i + 1].height, f);
                Vector3 centre = local + Vector3.up * Mathf.Min(1.2f, height * 0.4f);

                Vector3 world = gen.transform.TransformPoint(centre);
                if (hasPrevious)
                {
                    foreach (float side in lane)
                    {
                        Vector3 offset = gen.transform.right * (side * width);
                        RaycastHit[] hits = Physics.RaycastAll(
                            previous + offset,
                            (world - previous).normalized,
                            Vector3.Distance(previous, world));

                        foreach (RaycastHit hit in hits)
                        {
                            if (hit.collider.gameObject == gen.gameObject) continue;

                            string nm = hit.collider.gameObject.name;
                            if (!found.ContainsKey(nm)) found[nm] = 0;
                            found[nm]++;
                        }
                    }
                }

                previous = world;
                hasPrevious = true;
            }
            return found;
        }

        /// <summary>Every terrain a cave's footprint touches, so a cave spanning a tile seam works.</summary>
        public static List<Terrain> TerrainsUnder(CaveTunnelGenerator gen)
        {
            var hits = new List<Terrain>();
            if (gen == null || gen.Volume == null) return hits;

            Bounds world = TransformBounds(gen.transform, gen.Volume.LocalBounds);

            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 min = t.transform.position;
                Vector3 max = min + t.terrainData.size;
                if (world.max.x < min.x || world.min.x > max.x) continue;
                if (world.max.z < min.z || world.min.z > max.z) continue;
                hits.Add(t);
            }
            return hits;
        }
    }
}
