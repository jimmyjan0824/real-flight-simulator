using System.Collections.Generic;
using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Raycast landing gear (no WheelCollider, so no extra Unity module is needed).
    ///
    /// Each wheel casts a ray down the aircraft's -up axis. If it hits the ground the
    /// strut is compressed and produces a spring + damper force along the ground normal,
    /// plus tyre friction:
    ///   - rolling direction: rolling resistance, or brake friction on the main wheels
    ///   - sideways: strong lateral grip (keeps the aircraft tracking straight)
    /// The nose wheel steers with the rudder input, fading out as speed increases.
    ///
    /// All aircraft colliders are on the built-in "Ignore Raycast" layer, so these rays
    /// only ever hit the world.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class LandingGear : MonoBehaviour
    {
        private sealed class Wheel
        {
            public string Name;
            public Vector3 Attach;          // strut top, aircraft local space
            public bool Steerable;
            public bool Braked;
            public bool IsNose;
            public Transform Visual;
            public Quaternion VisualBaseRotation;
            public float Compression;       // metres, 0 = fully extended
            public float LastCompression;
            public float Length;            // current strut length, metres
            public bool Grounded;
        }

        [SerializeField] private float restLength = 0.7f;        // m, strut length fully extended
        [SerializeField] private float wheelRadius = 0.3f;       // m
        [SerializeField] private float springRate = 30000f;      // N/m
        [SerializeField] private float damperRate = 3500f;       // N/(m/s)
        [SerializeField] private float rollingResistance = 0.02f;
        [SerializeField] private float brakeFriction = 0.7f;
        [SerializeField] private float lateralFriction = 0.9f;
        [SerializeField] private float maxSteerDeg = 35f;

        private readonly List<Wheel> _wheels = new List<Wheel>();
        private Rigidbody _rb;
        private float _steerDeg;

        /// <summary>-1..1, set by AircraftController from the yaw input.</summary>
        public float Steer { get; set; }
        /// <summary>0..1, set by AircraftController.</summary>
        public float Brake { get; set; }

        public int GroundedCount { get; private set; }
        public int MainGroundedCount { get; private set; }
        public bool NoseGrounded { get; private set; }
        public bool AnyGrounded => GroundedCount > 0;

        public float RestLength => restLength;
        public float WheelRadius => wheelRadius;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        public void AddWheel(string wheelName, Vector3 attach, bool steerable, bool braked, bool isNose, Transform visual)
        {
            _wheels.Add(new Wheel
            {
                Name = wheelName,
                Attach = attach,
                Steerable = steerable,
                Braked = braked,
                IsNose = isNose,
                Visual = visual,
                VisualBaseRotation = visual != null ? visual.localRotation : Quaternion.identity,
                Length = restLength,
            });
        }

        public void ResetState()
        {
            foreach (var w in _wheels)
            {
                w.Compression = 0f;
                w.LastCompression = 0f;
                w.Length = restLength;
                w.Grounded = false;
            }
            GroundedCount = 0;
            MainGroundedCount = 0;
            NoseGrounded = false;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            float dt = Time.fixedDeltaTime;
            Vector3 up = transform.up;
            float speed = _rb.linearVelocity.magnitude;
            _steerDeg = Mathf.Clamp(Steer, -1f, 1f) * maxSteerDeg * Mathf.Lerp(1f, 0.2f, Mathf.Clamp01(speed / 30f));

            int grounded = 0, mains = 0;
            bool nose = false;
            float maxDist = restLength + wheelRadius;

            foreach (var w in _wheels)
            {
                Vector3 origin = transform.TransformPoint(w.Attach);
                w.LastCompression = w.Compression;

                if (!Physics.Raycast(origin, -up, out RaycastHit hit, maxDist,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    w.Grounded = false;
                    w.Compression = 0f;
                    w.Length = restLength;
                    continue;
                }

                w.Grounded = true;
                // Length can go slightly negative if the strut bottoms out; the spring
                // keeps pushing harder, and the fuselage collider is the hard stop.
                w.Length = hit.distance - wheelRadius;
                w.Compression = restLength - w.Length;

                float compressionVelocity = (w.Compression - w.LastCompression) / dt;
                float normalForce = Mathf.Max(0f, springRate * w.Compression + damperRate * compressionVelocity);

                Vector3 n = hit.normal;
                Vector3 fwd = transform.forward;
                if (w.Steerable) fwd = Quaternion.AngleAxis(_steerDeg, up) * fwd;
                fwd = Vector3.ProjectOnPlane(fwd, n).normalized;
                Vector3 side = Vector3.Cross(n, fwd); // aircraft right, along the ground

                Vector3 pointVel = _rb.GetPointVelocity(hit.point);
                float vFwd = Vector3.Dot(pointVel, fwd);
                float vSide = Vector3.Dot(pointVel, side);

                // Friction saturates smoothly near zero speed so the aircraft can sit still
                // without jittering back and forth.
                float mu = rollingResistance + (w.Braked ? Mathf.Clamp01(Brake) * brakeFriction : 0f);
                float fFwd = -Mathf.Clamp(vFwd / 0.5f, -1f, 1f) * mu * normalForce;
                float fSide = -Mathf.Clamp(vSide / 0.3f, -1f, 1f) * lateralFriction * normalForce;

                _rb.AddForceAtPosition(n * normalForce + fwd * fFwd + side * fSide, hit.point);

                grounded++;
                if (w.IsNose) nose = true;
                else mains++;
            }

            GroundedCount = grounded;
            MainGroundedCount = mains;
            NoseGrounded = nose;
        }

        private void Update()
        {
            foreach (var w in _wheels)
            {
                if (w.Visual == null) continue;
                float len = Mathf.Clamp(w.Length, 0f, restLength);
                w.Visual.localPosition = w.Attach + Vector3.down * len;
                w.Visual.localRotation = w.Steerable
                    ? Quaternion.Euler(0f, _steerDeg, 0f) * w.VisualBaseRotation
                    : w.VisualBaseRotation;
            }
        }
    }
}
