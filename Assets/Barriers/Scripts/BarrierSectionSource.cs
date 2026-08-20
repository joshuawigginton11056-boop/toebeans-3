using System.Collections.Generic;
using UnityEngine;

namespace Barriers
{
    /// <summary>
    /// A barrier prefab read once and kept ready to bend: its geometry flattened into the root's
    /// space, already cut into rings, plus the materials and renderer settings a bent copy has to
    /// keep wearing.
    ///
    /// Read once per build rather than once per placement, because the expensive parts — pulling
    /// the vertex arrays off the meshes and subdividing them — depend only on the prefab. Thirty
    /// sections off the same model share one template and differ only in the bend applied to a
    /// copy of it.
    ///
    /// This is the half of the bend that has to touch native types, which is why it is not in
    /// <see cref="BarrierSectionBender"/>.
    /// </summary>
    public sealed class BarrierSectionSource
    {
        /// <summary>Geometry in the prefab root's space, cut into rings and ready to copy and bend.</summary>
        public readonly BarrierSectionBuffer Template = new BarrierSectionBuffer();

        /// <summary>One per submesh of <see cref="Template"/>, in the same order.</summary>
        public readonly List<Material> Materials = new List<Material>();

        /// <summary>Which way the model runs and how long it is, in the prefab root's space.</summary>
        public BarrierSectionBender.SectionAxes Axes;

        /// <summary>Renderer whose shadow and probe settings a bent copy inherits.</summary>
        public MeshRenderer TemplateRenderer;

        public bool IsValid { get { return !Template.IsEmpty && Axes.Length > 1e-4f; } }

        /// <summary>
        /// The prefab's extent along the axis it runs down, measured in the root's own space.
        ///
        /// A section placed with facing Along Path runs down its local Z, so that is the span that
        /// has to match the gap; a model authored sideways is caught by the X test and reported as
        /// such, and everything downstream turns it to suit rather than placing it across the line.
        /// </summary>
        public static bool Measure(GameObject prefab, out BarrierSectionBender.SectionAxes axes)
        {
            axes = default(BarrierSectionBender.SectionAxes);
            axes.Along = 2;
            if (prefab == null) return false;

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            bool any = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled) continue;

                // Renderer bounds are world space, which on a prefab asset is the prefab's own —
                // but only while the root sits at the origin unrotated. Pushing the corners through
                // the root's inverse makes the measurement survive a prefab built on an offset.
                Bounds b = renderers[i].bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z);

                    Vector3 local = toRoot.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }

            if (!any) return false;

            Vector3 size = max - min;
            axes.Along = (size.z < 0.01f || size.x > size.z * 1.5f) ? 0 : 2;
            axes.Min = axes.Along == 0 ? min.x : min.z;
            axes.Length = axes.Along == 0 ? size.x : size.z;
            return axes.Length > 1e-4f;
        }

        /// <summary>
        /// Reads a prefab into a bendable template.
        /// </summary>
        /// <param name="prefab">The section to read.</param>
        /// <param name="ringSpacing">How finely it is cut along its length, in metres.</param>
        /// <param name="vertexBudget">Ceiling on the cut, so a dense model cannot hang the editor.</param>
        public static BarrierSectionSource Extract(GameObject prefab, float ringSpacing, int vertexBudget)
        {
            var source = new BarrierSectionSource();
            if (prefab == null) return source;

            BarrierSectionBender.SectionAxes axes;
            if (!Measure(prefab, out axes)) return source;
            source.Axes = axes;

            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var colours = new List<Color>();
            var indices = new List<int>();

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int f = 0; f < filters.Length; f++)
            {
                MeshFilter filter = filters[f];
                if (filter == null || !filter.gameObject.activeSelf) continue;

                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                if (source.TemplateRenderer == null) source.TemplateRenderer = renderer;

                // Model meshes are import-time read-only in a player, but the Editor always keeps
                // a readable copy — and building a run is an editor job.
                if (!mesh.isReadable && Application.isPlaying)
                {
                    Debug.LogWarning(string.Format(
                        "Barrier Line: {0} is not marked Read/Write Enabled, so it cannot be bent at " +
                        "runtime. Build the run in the editor, or tick Read/Write on the model.",
                        mesh.name), filter);
                    continue;
                }

                positions.Clear(); normals.Clear(); tangents.Clear();
                uv0.Clear(); uv1.Clear(); colours.Clear();

                mesh.GetVertices(positions);
                mesh.GetNormals(normals);
                mesh.GetTangents(tangents);
                mesh.GetUVs(0, uv0);
                mesh.GetUVs(1, uv1);
                mesh.GetColors(colours);

                Matrix4x4 toSection = toRoot * filter.transform.localToWorldMatrix;
                int vertexOffset = source.Template.Vertices.Count;

                for (int i = 0; i < positions.Count; i++)
                {
                    BarrierVertex v;
                    v.Position = toSection.MultiplyPoint3x4(positions[i]);
                    v.Normal = i < normals.Count ? toSection.MultiplyVector(normals[i]).normalized : Vector3.up;
                    v.Uv0 = i < uv0.Count ? uv0[i] : Vector2.zero;
                    v.Uv1 = i < uv1.Count ? uv1[i] : Vector2.zero;
                    v.Colour = i < colours.Count ? colours[i] : Color.white;

                    if (i < tangents.Count)
                    {
                        Vector3 t = toSection.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z));
                        t = t.normalized;
                        v.Tangent = new Vector4(t.x, t.y, t.z, tangents[i].w);
                    }
                    else v.Tangent = new Vector4(1f, 0f, 0f, 1f);

                    source.Template.Vertices.Add(v);
                }

                if (uv1.Count > 0) source.Template.HasUv1 = true;
                if (colours.Count > 0) source.Template.HasColour = true;

                Material[] mats = renderer.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    indices.Clear();
                    mesh.GetTriangles(indices, s);
                    if (indices.Count == 0) continue;

                    var tris = new List<int>(indices.Count);
                    for (int i = 0; i < indices.Count; i++) tris.Add(indices[i] + vertexOffset);

                    source.Template.Submeshes.Add(tris);
                    source.Materials.Add(mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null);
                }
            }

            BarrierSectionBender.SubdivideAlong(source.Template, source.Axes.Along, ringSpacing, vertexBudget);
            return source;
        }

        /// <summary>Turns a bent buffer into a mesh. The caller owns it and has to destroy it.</summary>
        public static Mesh ToMesh(BarrierSectionBuffer buffer, string name)
        {
            if (buffer == null || buffer.IsEmpty) return null;

            int count = buffer.Vertices.Count;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var tangents = new Vector4[count];
            var uv0 = new Vector2[count];
            var uv1 = buffer.HasUv1 ? new Vector2[count] : null;
            var colours = buffer.HasColour ? new Color[count] : null;

            for (int i = 0; i < count; i++)
            {
                BarrierVertex v = buffer.Vertices[i];
                positions[i] = v.Position;
                normals[i] = v.Normal;
                tangents[i] = v.Tangent;
                uv0[i] = v.Uv0;
                if (uv1 != null) uv1[i] = v.Uv1;
                if (colours != null) colours[i] = v.Colour;
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uv0;
            if (uv1 != null) mesh.uv2 = uv1;
            if (colours != null) mesh.colors = colours;

            mesh.subMeshCount = buffer.Submeshes.Count;
            for (int s = 0; s < buffer.Submeshes.Count; s++)
                mesh.SetTriangles(buffer.Submeshes[s], s, false);

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Copies the source renderer's shadow and probe settings onto a bent copy.</summary>
        public void ApplyRendererSettings(MeshRenderer target)
        {
            if (target == null) return;

            target.sharedMaterials = Materials.ToArray();
            if (TemplateRenderer == null) return;

            target.shadowCastingMode = TemplateRenderer.shadowCastingMode;
            target.receiveShadows = TemplateRenderer.receiveShadows;
            target.lightProbeUsage = TemplateRenderer.lightProbeUsage;
            target.reflectionProbeUsage = TemplateRenderer.reflectionProbeUsage;
            target.motionVectorGenerationMode = TemplateRenderer.motionVectorGenerationMode;
            target.allowOcclusionWhenDynamic = TemplateRenderer.allowOcclusionWhenDynamic;
        }
    }
}
