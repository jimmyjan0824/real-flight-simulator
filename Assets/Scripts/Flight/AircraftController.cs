using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Flight model for a light single-engine trainer (roughly a 1-tonne high-wing aircraft).
    ///
    /// Forces (applied at the centre of mass every FixedUpdate):
    ///   Lift    = q·S·CL(α)        perpendicular to the airflow, in the plane of symmetry
    ///   Drag    = q·S·(CD0 + k·CL²) opposite to the airflow (plus flat-plate drag when stalled)
    ///   Side    = q·S·CYβ·β         from the fuselage and fin in a sideslip
    ///   Thrust  = Tmax·throttle·(1 − V/Vprop) along the nose
    ///   Gravity = handled by the Rigidbody
    /// where q = ½ρV² (dynamic pressure), S = wing area, α = angle of attack, β = sideslip.
    ///
    /// Moments use classic stability derivatives, so the aircraft is naturally stable
    /// (weathervanes into the wind, pitch returns to trim, rolls are damped):
    ///   Pitch  M = q·S·c·(Cm0 + Cmα·α + Cmq·q̂ + Cmδe·elevator)
    ///   Roll   L = q·S·b·(Clβ·β + Clp·p̂ + Clr·r̂ + Clδa·aileron)
    ///   Yaw    N = q·S·b·(Cnβ·β + Cnr·r̂ + Cnδr·rudder)
    /// (p̂, q̂, r̂ are non-dimensional body rates: rate·b/2V or rate·c/2V.)
    ///
    /// Aero sign convention is the standard one (nose up, right wing down, nose right are
    /// positive). Unity is left-handed, so those map to local −X, −Z and +Y torque.
    /// </summary>
    [DefaultExecutionOrder(0)]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(LandingGear))]
    public sealed class AircraftController : MonoBehaviour
    {
        // ------------------------------------------------------------ pilot inputs
        /// <summary>-1..1, positive = pull back (nose up).</summary>
        public float Pitch;
        /// <summary>-1..1, positive = roll right.</summary>
        public float Roll;
        /// <summary>-1..1, positive = yaw right (also steers the nose wheel).</summary>
        public float Yaw;
        /// <summary>0..1.</summary>
        public float Throttle;
        /// <summary>0..1, wheel brakes.</summary>
        public float Brake;
        /// <summary>Elevator trim, added to Pitch. Range ±TrimLimit.</summary>
        public float PitchTrim;
        public int FlapStage;
        public bool EngineRunning = true;

        public const float TrimLimit = 0.5f;
        public static readonly int[] FlapDegrees = { 0, 10, 30 };
        private static readonly float[] FlapLift = { 0f, 0.25f, 0.55f };
        private static readonly float[] FlapDrag = { 0f, 0.006f, 0.03f };
        private static readonly float[] FlapPitch = { 0f, -0.015f, -0.04f };

        // ------------------------------------------------------------ airframe
        [Header("Mass & geometry")]
        [SerializeField] private float mass = 1000f;                 // kg
        [SerializeField] private Vector3 inertia = new Vector3(1825f, 2667f, 1285f); // kg·m² (pitch, yaw, roll)
        [SerializeField] private float wingArea = 16.2f;             // m²
        [SerializeField] private float wingSpan = 11f;               // m
        [SerializeField] private float chord = 1.5f;                 // m
        [SerializeField] private float airDensity = 1.225f;          // kg/m³, sea level

        [Header("Engine")]
        [SerializeField] private float maxThrust = 3500f;            // N, static
        [SerializeField] private float propZeroThrustSpeed = 90f;    // m/s, thrust fades to 0 here
        [SerializeField] private float engineSpoolTime = 1.2f;       // s, idle -> full

        [Header("Lift & drag")]
        [SerializeField] private float cl0 = 0.25f;
        [SerializeField] private float clAlpha = 5.0f;               // per rad
        [SerializeField] private float stallAlphaDeg = 15f;
        [SerializeField] private float stallAlphaNegDeg = 12f;
        [SerializeField] private float cd0 = 0.027f;
        [SerializeField] private float inducedDragK = 0.053f;        // 1/(π·e·AR)
        [SerializeField] private float cyBeta = -1.0f;

        [Header("Pitch")]
        [SerializeField] private float cm0 = 0.035f;
        [SerializeField] private float cmAlpha = -1.2f;
        [SerializeField] private float cmQ = -12f;
        [SerializeField] private float cmElevator = 0.6f;

        [Header("Roll")]
        [SerializeField] private float clBeta = -0.1f;
        [SerializeField] private float clP = -0.45f;
        [SerializeField] private float clR = 0.1f;
        [SerializeField] private float clAileron = 0.07f;

        [Header("Yaw")]
        [SerializeField] private float cnBeta = 0.12f;
        [SerializeField] private float cnR = -0.1f;
        [SerializeField] private float cnRudder = 0.035f;

        /// <summary>Height of the centre of mass above the ground when sitting on the gear.</summary>
        public const float CgHeightOnGround = 1.45f;

        public Transform Propeller;

        private Rigidbody _rb;
        private LandingGear _gear;
        private float _engine;       // spooled throttle 0..1
        private float _alpha, _beta;
        private float _aglM;

        public Rigidbody Body => _rb;
        public LandingGear Gear => _gear;

        // ------------------------------------------------------------ telemetry
        public float AirspeedMs => _rb.linearVelocity.magnitude;   // no wind yet: airspeed == ground speed
        public float AirspeedKts => AirspeedMs * 1.943844f;
        public float GroundSpeedMs { get { var v = _rb.linearVelocity; v.y = 0f; return v.magnitude; } }
        public float AltitudeFt => transform.position.y * 3.28084f;
        public float AltitudeAglM => _aglM;
        public float VerticalSpeedFpm => _rb.linearVelocity.y * 196.8504f;
        public float PitchDeg => Mathf.Asin(Mathf.Clamp(transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        public float BankDeg => Mathf.Atan2(-transform.right.y, transform.up.y) * Mathf.Rad2Deg;
        public float HeadingDeg
        {
            get
            {
                Vector3 f = transform.forward;
                float h = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                return h < 0f ? h + 360f : h;
            }
        }
        public float AlphaDeg => _alpha * Mathf.Rad2Deg;
        public float EnginePower01 => _engine;
        public int FlapDeg => FlapDegrees[Mathf.Clamp(FlapStage, 0, FlapDegrees.Length - 1)];
        public bool StallWarning => !_gear.AnyGrounded && AirspeedMs > 5f && _alpha > (stallAlphaDeg - 2.5f) * Mathf.Deg2Rad;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _gear = GetComponent<LandingGear>();

            _rb.mass = mass;
            _rb.useGravity = true;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.05f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            // Set explicitly so adding/removing visual colliders never changes the handling.
            _rb.centerOfMass = Vector3.zero;
            _rb.inertiaTensorRotation = Quaternion.identity;
            _rb.inertiaTensor = inertia;
            _rb.maxAngularVelocity = 7f;
        }

        public void CycleFlaps()
        {
            FlapStage = (FlapStage + 1) % FlapDegrees.Length;
        }

        public void ResetTo(Vector3 position, Quaternion rotation, Vector3 velocity, int flapStage, float trim, float throttle)
        {
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Vector3.zero;

            FlapStage = flapStage;
            PitchTrim = trim;
            Throttle = throttle;
            _engine = throttle;
            EngineRunning = true;
            Pitch = Roll = Yaw = Brake = 0f;

            _gear.ResetState();
            Physics.SyncTransforms();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Ground steering and brakes are handled by the gear.
            _gear.Steer = Yaw;
            _gear.Brake = Brake;

            UpdateAgl();

            // ---------------------------------------------------- engine / thrust
            float target = EngineRunning ? Mathf.Clamp01(Throttle) : 0f;
            _engine = Mathf.MoveTowards(_engine, target, dt / engineSpoolTime);

            Vector3 vel = _rb.linearVelocity;
            Vector3 vLocal = transform.InverseTransformDirection(vel);
            float forwardSpeed = Mathf.Max(0f, vLocal.z);
            float thrust = maxThrust * _engine * Mathf.Clamp01(1f - forwardSpeed / propZeroThrustSpeed);
            _rb.AddForce(transform.forward * thrust);

            // ---------------------------------------------------- aerodynamics
            float V = vel.magnitude;
            if (V < 1f)
            {
                _alpha = 0f;
                _beta = 0f;
                return;
            }

            _alpha = Mathf.Atan2(-vLocal.y, vLocal.z);
            _beta = Mathf.Atan2(vLocal.x, new Vector2(vLocal.y, vLocal.z).magnitude);

            int flap = Mathf.Clamp(FlapStage, 0, FlapDegrees.Length - 1);
            float q = 0.5f * airDensity * V * V;

            float cl = LiftCoefficient(_alpha, FlapLift[flap], out float stall);
            float sinA = Mathf.Sin(_alpha);
            float cd = cd0 + FlapDrag[flap] + inducedDragK * cl * cl + stall * 1.2f * sinA * sinA;
            float cy = cyBeta * _beta;

            Vector3 velDir = vel / V;
            Vector3 liftDir = Vector3.Cross(velDir, transform.right).normalized;

            Vector3 aeroForce = liftDir * (q * wingArea * cl)
                                - velDir * (q * wingArea * cd)
                                + transform.right * (q * wingArea * cy);
            _rb.AddForce(aeroForce);

            // ---------------------------------------------------- moments
            Vector3 wLocal = transform.InverseTransformDirection(_rb.angularVelocity);
            float p = -wLocal.z;   // roll rate, right wing down +
            float qr = -wLocal.x;  // pitch rate, nose up +
            float r = wLocal.y;    // yaw rate, nose right +
            float inv2V = 1f / (2f * V);

            float elevator = Mathf.Clamp(Pitch + PitchTrim, -1f, 1f);
            float aileron = Mathf.Clamp(Roll, -1f, 1f);
            float rudder = Mathf.Clamp(Yaw, -1f, 1f);
            float alphaForMoment = Mathf.Clamp(_alpha, -0.6f, 0.6f);

            float cm = cm0 + FlapPitch[flap] + cmAlpha * alphaForMoment + cmQ * qr * chord * inv2V + cmElevator * elevator;
            float cRoll = clBeta * _beta + clP * p * wingSpan * inv2V + clR * r * wingSpan * inv2V + clAileron * aileron;
            float cn = cnBeta * _beta + cnR * r * wingSpan * inv2V + cnRudder * rudder;

            float qS = q * wingArea;
            var torque = new Vector3(
                -qS * chord * cm,       // pitch
                qS * wingSpan * cn,     // yaw
                -qS * wingSpan * cRoll  // roll
            );
            _rb.AddRelativeTorque(torque);
        }

        /// <summary>
        /// Linear lift up to the stall angle, then blends over ~6° towards flat-plate
        /// behaviour (CL ≈ 1.05·sin 2α), which gives a clear lift break at the stall.
        /// </summary>
        private float LiftCoefficient(float alpha, float flapCl, out float stall)
        {
            float aPos = stallAlphaDeg * Mathf.Deg2Rad;
            float aNeg = -stallAlphaNegDeg * Mathf.Deg2Rad;
            float c0 = cl0 + flapCl;
            float flat = 1.05f * Mathf.Sin(2f * alpha);
            const float blendRange = 6f * Mathf.Deg2Rad;

            if (alpha > aPos)
            {
                stall = Mathf.Clamp01((alpha - aPos) / blendRange);
                return Mathf.Lerp(c0 + clAlpha * aPos, flat, stall);
            }
            if (alpha < aNeg)
            {
                stall = Mathf.Clamp01((aNeg - alpha) / blendRange);
                return Mathf.Lerp(c0 + clAlpha * aNeg, flat, stall);
            }
            stall = 0f;
            return c0 + clAlpha * alpha;
        }

        private void UpdateAgl()
        {
            Vector3 pos = _rb.position;
            if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 5000f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                _aglM = Mathf.Max(0f, hit.distance - CgHeightOnGround);
            else
                _aglM = Mathf.Max(0f, pos.y - CgHeightOnGround);
        }

        private void Update()
        {
            if (Propeller != null)
            {
                float degPerSec = EngineRunning ? Mathf.Lerp(600f, 2500f, _engine) : 0f;
                Propeller.Rotate(0f, 0f, degPerSec * Time.deltaTime, Space.Self);
            }
        }
    }
}
