using UnityEngine;

namespace Toebeans.SnowTrees
{
    /// <summary>
    /// Grows one of the Toebeans 3 snow trees onto this GameObject's
    /// MeshFilter. Runs in edit mode, so the tree reshapes as soon as a value
    /// changes in the inspector.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Toebeans/Snow Tree")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SnowTree : MonoBehaviour
    {
        [SerializeField] SnowTreeVariant variant = SnowTreeVariant.SnowSpruceA;

        [Tooltip("Ignore the variant preset and use the settings below instead.")]
        [SerializeField] bool customSettings;

        [SerializeField] SnowTreeSettings settings = SnowTreeSettings.ForVariant(SnowTreeVariant.SnowSpruceA);

        [Tooltip("Hard-edged faces (matches the stylised kit) at the cost of more vertices.")]
        [SerializeField] bool flatShading = true;

        [Tooltip("Rebuild whenever the object is enabled. Turn off once a mesh has been baked.")]
        [SerializeField] bool buildOnEnable = true;

        Mesh _mesh;

        public SnowTreeVariant Variant
        {
            get => variant;
            set
            {
                variant = value;
                if (!customSettings)
                {
                    settings = SnowTreeSettings.ForVariant(value);
                }

                Rebuild();
            }
        }

        /// <summary>The settings actually used for the next rebuild.</summary>
        public SnowTreeSettings EffectiveSettings =>
            customSettings ? settings : SnowTreeSettings.ForVariant(variant);

        void OnEnable()
        {
            if (buildOnEnable)
            {
                Rebuild();
            }
        }

        void OnDisable()
        {
            // The generated mesh is owned by this component, never an asset.
            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }

                _mesh = null;
            }
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

#if UNITY_EDITOR
            // Deferred: mesh work during OnValidate upsets the editor.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                {
                    Rebuild();
                }
            };
#else
            Rebuild();
#endif
        }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter == null)
            {
                return;
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = $"{name} (Snow Tree)", hideFlags = HideFlags.DontSave };
            }

            SnowTreeMeshBuilder.Build(EffectiveSettings, _mesh, flatShading);
            filter.sharedMesh = _mesh;
        }

        [ContextMenu("Randomise Seed")]
        public void RandomiseSeed()
        {
            if (!customSettings)
            {
                settings = SnowTreeSettings.ForVariant(variant);
                customSettings = true;
            }

            settings.seed = Random.Range(int.MinValue, int.MaxValue);
            Rebuild();
        }

        /// <summary>Builds a standalone, non-shared mesh - used by the baker.</summary>
        public Mesh CreateBakedMesh()
        {
            Mesh baked = SnowTreeMeshBuilder.Build(EffectiveSettings, flatShading);
            baked.name = customSettings ? name : variant.AssetName();
            return baked;
        }
    }
}
