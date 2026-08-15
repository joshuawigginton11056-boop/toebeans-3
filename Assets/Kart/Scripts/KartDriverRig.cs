using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>
    /// Keeps the driver's arms attached to the steering wheel. The hands are parented to the wheel, so
    /// they orbit with it for free; this solves the elbow and re-aims the two capsules per arm to
    /// follow. Without it the driver steers with their arms held out in front of them like a sleepwalker.
    /// </summary>
    [DisallowMultipleComponent]
    public class KartDriverRig : MonoBehaviour
    {
        [Header("Chain")]
        public Transform kartRoot;
        public Transform handLeft;
        public Transform handRight;
        public Transform upperArmLeft;
        public Transform forearmLeft;
        public Transform upperArmRight;
        public Transform forearmRight;

        [Header("Proportions")]
        [Tooltip("Shoulder position in kart local space, for the right side. The left mirrors it.")]
        public Vector3 shoulderLocal = KartBlueprint.Shoulder;
        public float upperArmLength = KartBlueprint.UpperArmLength;
        public float forearmLength = KartBlueprint.ForearmLength;
        [Tooltip("Which way the elbow bends, in kart local space, for the right side.")]
        public Vector3 elbowDirection = new Vector3(1f, -0.55f, -0.6f);

        void LateUpdate()
        {
            if (kartRoot == null)
                return;

            Solve(-1, handLeft, upperArmLeft, forearmLeft);
            Solve(1, handRight, upperArmRight, forearmRight);
        }

        void Solve(int side, Transform hand, Transform upperArm, Transform forearm)
        {
            if (hand == null || upperArm == null || forearm == null)
                return;

            Vector3 shoulder = kartRoot.TransformPoint(
                new Vector3(shoulderLocal.x * side, shoulderLocal.y, shoulderLocal.z));
            Vector3 target = hand.position;

            Vector3 toHand = target - shoulder;
            float reach = toHand.magnitude;
            if (reach < 0.0001f)
                return;

            float a = upperArmLength;
            float b = forearmLength;

            // Out of reach means the arm is simply straight; without this clamp the cosine rule below
            // goes imaginary and the elbow snaps to a random place.
            float clamped = Mathf.Clamp(reach, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
            Vector3 axis = toHand / reach;

            // Cosine rule: distance along the shoulder-to-hand line at which the elbow sits.
            float along = (clamped * clamped + a * a - b * b) / (2f * clamped);
            float outward = Mathf.Sqrt(Mathf.Max(a * a - along * along, 0f));

            Vector3 pole = kartRoot.TransformDirection(
                new Vector3(elbowDirection.x * side, elbowDirection.y, elbowDirection.z));
            Vector3 perpendicular = pole - axis * Vector3.Dot(pole, axis);
            if (perpendicular.sqrMagnitude < 0.0001f)
                perpendicular = Vector3.Cross(axis, kartRoot.up);
            perpendicular.Normalize();

            Vector3 elbow = shoulder + axis * along + perpendicular * outward;

            AimCapsule(upperArm, shoulder, elbow);
            AimCapsule(forearm, elbow, target);
        }

        /// <summary>Stretches a capsule between two world points along its own local Y.</summary>
        static void AimCapsule(Transform capsule, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.0001f)
                return;

            capsule.position = (from + to) * 0.5f;
            capsule.rotation = Quaternion.FromToRotation(Vector3.up, delta / length);

            // Unity's capsule primitive is two units tall, so half the length is the scale.
            Vector3 scale = capsule.localScale;
            scale.y = length * 0.5f;
            capsule.localScale = scale;
        }
    }
}
