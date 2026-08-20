using System.Collections.Generic;
using UnityEngine;

namespace Toebeans
{
    /// <summary>
    /// Holds the meshes a generator built for one object, as sub-assets of a single file.
    ///
    /// It exists because a mesh with no asset behind it gets serialised **inside the scene**, and
    /// procedural geometry is big: LavaWorld carried 124 of them and they were 91% of a 56 MB
    /// scene file. Moving them out means a scene edit writes a small diff instead of rewriting
    /// four hundred thousand vertices, and the geometry only changes when it is actually rebuilt.
    ///
    /// One library per root object rather than one per scene, so regenerating a barrier line does
    /// not rewrite the volcano's geometry as collateral - which matters because these files are
    /// LFS-tracked and LFS re-uploads whole objects, not diffs.
    ///
    /// This is a runtime type on purpose. The meshes are referenced by the scene and pulled into
    /// builds normally; the container only needs to be loadable so those references resolve.
    /// </summary>
    public sealed class GeneratedMeshLibrary : ScriptableObject
    {
        [Tooltip("Scene this library was extracted from, for tracing a stray file back to its owner.")]
        public string sourceScene = string.Empty;

        [Tooltip("Root object in that scene whose geometry this holds.")]
        public string ownerObject = string.Empty;

        [Tooltip("The meshes, which are also sub-assets of this file. Listed so the set is " +
                 "visible in the inspector and so something still references them if the scene does not.")]
        public List<Mesh> meshes = new List<Mesh>();
    }
}
