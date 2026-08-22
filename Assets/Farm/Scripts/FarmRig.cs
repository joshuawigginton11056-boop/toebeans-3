using UnityEngine;

namespace Farm
{
    /// <summary>
    /// Finding the named parts of a generated model, and posing them relative to how they were
    /// authored.
    ///
    /// Every multi-part model in the farm pack ships with a fixed set of part names — Body, Head,
    /// Jaw, Leg_FL and so on — written down in the Blender script that makes it and read back here.
    /// See <c>Tools/blender/models/farm_animals.py</c> for the contract.
    /// </summary>
    public static class FarmRig
    {
        /// <summary>The first descendant with this name, or null. Depth-first, inactive included.</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) return all[i];
            }
            return null;
        }
    }

    /// <summary>
    /// One posable part, remembering the rotation it was authored at.
    ///
    /// Poses are applied as an offset from the rest pose rather than as an absolute rotation, which
    /// matters because a part's local rotation is not necessarily identity: the rig is rebuilt in
    /// Unity by re-parenting (see FarmAssetSetup.RebuildRig), and re-parenting under an already
    /// rotated joint leaves a local rotation behind. Writing absolute rotations here would snap
    /// every such part to a pose the modeller never drew.
    /// </summary>
    public struct FarmJoint
    {
        public Transform Transform;
        Quaternion _rest;
        Vector3 _restPosition;

        public bool Ok { get { return Transform != null; } }

        public static FarmJoint Bind(Transform root, string name)
        {
            var joint = new FarmJoint { Transform = FarmRig.Find(root, name) };
            if (joint.Transform != null)
            {
                joint._rest = joint.Transform.localRotation;
                joint._restPosition = joint.Transform.localPosition;
            }
            return joint;
        }

        /// <summary>Rotate about the part's own axes, from rest.</summary>
        public void Pose(float x, float y, float z)
        {
            if (Transform == null) return;
            Transform.localRotation = _rest * Quaternion.Euler(x, y, z);
        }

        public void Pose(Quaternion offset)
        {
            if (Transform == null) return;
            Transform.localRotation = _rest * offset;
        }

        /// <summary>Shift the part from where it was authored. Used for body bob and float.</summary>
        public void Offset(Vector3 delta)
        {
            if (Transform == null) return;
            Transform.localPosition = _restPosition + delta;
        }

        public void Rest()
        {
            if (Transform == null) return;
            Transform.localRotation = _rest;
            Transform.localPosition = _restPosition;
        }
    }
}
