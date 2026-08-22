using UnityEngine;

namespace Farm
{
    /// <summary>
    /// Turns the wheels of a tractor, pickup or wagon off the distance the vehicle has actually
    /// moved, and points the front pair where it is going.
    ///
    /// The whole component is one idea: a wheel's rotation is not an animation, it is a
    /// consequence. Spin a wheel on a timer and it is right at exactly one speed and visibly wrong
    /// at every other — and a parked vehicle with spinning wheels is worse than one with still
    /// ones. Driving it off travel makes it correct for free whether the vehicle is driven by a
    /// script, dragged in the editor, or towed behind something else.
    ///
    /// It is also what makes the hay wagon work with no extra code: park it behind the tractor,
    /// move the tractor, and the wagon's wheels turn because the wagon moved.
    ///
    /// Unlike the rest of the farm this one does integrate — it has to, because it is following
    /// something it does not control and cannot predict. That is fine: a wheel's angle is not
    /// gameplay, nothing else reads it, and it self-corrects the moment the vehicle stops.
    ///
    /// Part names are the contract in <c>Tools/blender/models/farm_vehicles.py</c>: Body,
    /// Wheel_FL, Wheel_FR, Wheel_RL, Wheel_RR, and Steering on models that have one.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Toebeans/Farm/Farm Vehicle")]
    public sealed class FarmVehicle : MonoBehaviour
    {
        [Header("Wheels")]
        [Tooltip("Radius of the rear wheels, in metres. 0 measures it off the model at Awake, " +
                 "which is right unless you have scaled the prefab.")]
        public float rearRadius;

        [Tooltip("Radius of the front wheels. 0 measures it off the model.")]
        public float frontRadius;

        [Tooltip("How far the front wheels turn at full lock, in degrees.")]
        public float steerDegrees = 26f;

        [Tooltip("How quickly the steering catches up, in degrees per second.")]
        public float steerRate = 90f;

        [Header("Steering wheel")]
        [Tooltip("Turns of the steering wheel per full lock at the road wheels.")]
        public float steeringRatio = 2.4f;

        FarmJoint _steering;
        FarmJoint[] _front = new FarmJoint[0];
        FarmJoint[] _rear = new FarmJoint[0];

        float _frontAngle;
        float _rearAngle;
        float _steer;
        Vector3 _lastPosition;
        bool _bound;

        void Awake()
        {
            Bind();
            _lastPosition = transform.position;
        }

        void Bind()
        {
            Transform t = transform;
            _steering = FarmJoint.Bind(t, "Steering");

            _front = Collect(t, "Wheel_FL", "Wheel_FR");
            _rear = Collect(t, "Wheel_RL", "Wheel_RR");

            if (frontRadius <= 0f) frontRadius = MeasureRadius(_front, 0.36f);
            if (rearRadius <= 0f) rearRadius = MeasureRadius(_rear, 0.36f);

            _bound = true;
        }

        static FarmJoint[] Collect(Transform root, params string[] names)
        {
            var found = new System.Collections.Generic.List<FarmJoint>(names.Length);
            foreach (string n in names)
            {
                FarmJoint j = FarmJoint.Bind(root, n);
                if (j.Ok) found.Add(j);
            }
            return found.ToArray();
        }

        /// <summary>
        /// The wheel's radius, taken from its renderer rather than from a number typed twice.
        ///
        /// The Blender side already knows every wheel's radius, and duplicating it here is exactly
        /// the drift the manifest exists to avoid — a tractor re-exported on bigger tyres would
        /// otherwise keep spinning at the old rate, which reads as the wheels slipping.
        /// </summary>
        static float MeasureRadius(FarmJoint[] wheels, float fallback)
        {
            foreach (FarmJoint w in wheels)
            {
                if (!w.Ok) continue;
                var r = w.Transform.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                // Vertical extent, because a wheel is as tall as it is long and its width is the
                // one dimension that is not the diameter.
                float d = r.bounds.size.y;
                if (d > 0.02f) return d * 0.5f;
            }
            return fallback;
        }

        void LateUpdate()
        {
            if (!_bound) Bind();

            Vector3 now = transform.position;
            Vector3 delta = now - _lastPosition;
            _lastPosition = now;

            // Signed along the vehicle's own forward, so reversing turns the wheels backwards.
            float travel = Vector3.Dot(delta, transform.forward);
            float sideways = Vector3.Dot(delta, transform.right);

            _frontAngle += Degrees(travel, frontRadius);
            _rearAngle += Degrees(travel, rearRadius);

            // Steer toward whatever direction the vehicle is actually sliding, which handles being
            // driven, dragged in the editor and towed round a corner with the same two lines.
            float want = 0f;
            if (delta.sqrMagnitude > 1e-8f)
            {
                float slip = Mathf.Atan2(sideways, Mathf.Abs(travel) + 1e-4f) * Mathf.Rad2Deg;
                want = Mathf.Clamp(slip, -steerDegrees, steerDegrees);
                if (travel < 0f) want = -want;
            }
            _steer = Mathf.MoveTowards(_steer, want, steerRate * Time.deltaTime);

            foreach (FarmJoint w in _front) w.Pose(Quaternion.Euler(0f, _steer, 0f) *
                                                   Quaternion.Euler(_frontAngle, 0f, 0f));
            foreach (FarmJoint w in _rear) w.Pose(_rearAngle, 0f, 0f);

            if (_steering.Ok && steerDegrees > 0.01f)
            {
                float turns = (_steer / steerDegrees) * steeringRatio * 180f;
                // About the column, which is the steering wheel's own local Y — the model is
                // authored tilted onto the column for exactly this reason. See kart_buggy.py,
                // which learned it the hard way: authored flat, it turns like a tabletop.
                _steering.Pose(0f, turns, 0f);
            }
        }

        static float Degrees(float travel, float radius)
        {
            if (radius <= 0.001f) return 0f;
            return travel / (2f * Mathf.PI * radius) * 360f;
        }
    }
}
