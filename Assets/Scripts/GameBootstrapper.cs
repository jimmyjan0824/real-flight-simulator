using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Code-first entry point. The main scene contains only one GameObject with this
    /// component; everything else (world, aircraft, camera, controls, HUD) is built at runtime.
    ///
    /// Stage 1: flat ground + one runway, a block aircraft with a real-ish flight model,
    /// touch/tilt controls, chase/cockpit cameras, HUD and landing judgement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrapper : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] private float groundSize = 20000f;     // metres
        [SerializeField] private float runwayLength = 2000f;    // metres
        [SerializeField] private float runwayWidth = 45f;       // metres
        [SerializeField] private int sceneryBlocks = 220;

        [Header("Approach start")]
        [SerializeField] private float approachDistance = 3300f;   // m from the aiming point
        [SerializeField] private float approachSpeedMs = 36f;      // ~70 kt
        [SerializeField] private float glideSlopeDeg = 3f;

        private const float RunwaySurfaceY = 0.1f;
        private const float AimPointFromThreshold = 300f;

        private Runway _runway;
        private AircraftController _aircraft;
        private LandingJudge _judge;
        private FlightCamera _flightCamera;
        private FlightInput _input;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadowDistance = 300f;
            Time.fixedDeltaTime = 0.01f; // 100 Hz physics: stable gear springs and aero
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _runway = new Runway(Vector3.zero, runwayLength, runwayWidth, RunwaySurfaceY);

            BuildLighting();
            BuildGround();
            BuildRunway();
            BuildScenery();

            _aircraft = AircraftBuilder.Build(transform);
            _judge = _aircraft.gameObject.AddComponent<LandingJudge>();
            _judge.Init(_aircraft, _runway);

            BuildCamera();

            _input = gameObject.AddComponent<FlightInput>();
            _input.Init(_aircraft);
            _input.ResetRunwayRequested += ResetOnRunway;
            _input.ResetApproachRequested += ResetOnApproach;
            _input.ViewToggleRequested += _flightCamera.Toggle;

            var hud = gameObject.AddComponent<FlightHud>();
            hud.Init(_aircraft, _judge, _input);

            ResetOnRunway();
            PhysicsUtil.CheckGround(_aircraft.transform.position);
        }

        private void Update()
        {
            // Safety net: if the aircraft ever ends up below the world, put it back on the runway.
            if (_aircraft != null && _aircraft.transform.position.y < -20f)
            {
                Debug.LogError("[FlightSim] Aircraft fell below the ground; resetting. Ground colliders missing?");
                ResetOnRunway();
            }
        }

        // ---------------------------------------------------------------- resets

        private void ResetOnRunway()
        {
            // Lined up on the centreline just past the threshold, wheels slightly above the surface.
            Vector3 pos = _runway.Threshold + new Vector3(0f, AircraftController.CgHeightOnGround + 0.15f, 30f);
            _aircraft.ResetTo(pos, Quaternion.identity, Vector3.zero, flapStage: 0, trim: 0.05f, throttle: 0f);
            _input.SetThrottle(0f);
            _judge.ResetState(airborne: false);
            _flightCamera.Snap();
        }

        private void ResetOnApproach()
        {
            // On a 3° glide path towards the aiming point, flaps 10, trimmed for ~70 kt.
            Vector3 aim = _runway.Threshold + new Vector3(0f, 0f, AimPointFromThreshold);
            float height = Mathf.Tan(glideSlopeDeg * Mathf.Deg2Rad) * approachDistance;
            Vector3 pos = aim + new Vector3(0f, AircraftController.CgHeightOnGround + height, -approachDistance);
            Vector3 vel = Quaternion.Euler(glideSlopeDeg, 0f, 0f) * Vector3.forward * approachSpeedMs;
            const float throttle = 0.2f;
            _aircraft.ResetTo(pos, Quaternion.identity, vel, flapStage: 1, trim: 0.08f, throttle: throttle);
            _input.SetThrottle(throttle);
            _judge.ResetState(airborne: true);
            _flightCamera.Snap();
        }

        // ---------------------------------------------------------------- builders

        private void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.58f);
        }

        private void BuildGround()
        {
            // A thin cube (top face at y = 0) rather than a Plane: Plane needs a MeshCollider,
            // and every collider type must be referenced from code or the iOS build strips it.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localScale = new Vector3(groundSize, 1f, groundSize);
            ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            PhysicsUtil.EnsureBoxCollider(ground);
            Materials.Tint(ground, new Color(0.30f, 0.42f, 0.22f));
        }

        private void BuildRunway()
        {
            var runway = new GameObject("Runway");
            runway.transform.SetParent(transform, false);

            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "Surface";
            surface.transform.SetParent(runway.transform, false);
            surface.transform.localScale = new Vector3(runwayWidth, RunwaySurfaceY, runwayLength);
            surface.transform.localPosition = new Vector3(0f, RunwaySurfaceY * 0.5f, 0f);
            PhysicsUtil.EnsureBoxCollider(surface);
            Materials.Tint(surface, new Color(0.18f, 0.18f, 0.19f));

            float halfL = runwayLength * 0.5f;
            float markY = RunwaySurfaceY + 0.01f;

            // Threshold bars ("piano keys") at both ends.
            for (int end = -1; end <= 1; end += 2)
            {
                float z = end * (halfL - 21f);
                for (int i = 0; i < 8; i++)
                {
                    float x = -18.9f + i * 5.4f;
                    Marking(runway.transform, $"Threshold_{end}_{i}", new Vector3(x, markY, z), new Vector3(1.8f, 0.02f, 30f));
                }
                // Aiming point blocks, 300 m in from each threshold.
                float az = end * (halfL - AimPointFromThreshold - 30f);
                Marking(runway.transform, $"Aim_{end}_L", new Vector3(-9f, markY, az), new Vector3(6f, 0.02f, 60f));
                Marking(runway.transform, $"Aim_{end}_R", new Vector3(9f, markY, az), new Vector3(6f, 0.02f, 60f));
            }

            // Edge lines
            float edgeX = runwayWidth * 0.5f - 0.6f;
            Marking(runway.transform, "Edge_L", new Vector3(-edgeX, markY, 0f), new Vector3(0.9f, 0.02f, runwayLength - 4f));
            Marking(runway.transform, "Edge_R", new Vector3(edgeX, markY, 0f), new Vector3(0.9f, 0.02f, runwayLength - 4f));

            // Dashed centreline: 30 m stripes with 20 m gaps, between the threshold bars.
            const float stripeLen = 30f, gap = 20f;
            float start = -halfL + 60f;
            float end2 = halfL - 60f;
            int n = 0;
            for (float z = start; z + stripeLen <= end2; z += stripeLen + gap, n++)
                Marking(runway.transform, $"Centreline_{n:00}", new Vector3(0f, markY, z + stripeLen * 0.5f), new Vector3(0.9f, 0.02f, stripeLen));
        }

        private static void Marking(Transform parent, string markName, Vector3 pos, Vector3 size)
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m.name = markName;
            m.transform.SetParent(parent, false);
            m.transform.localScale = size;
            m.transform.localPosition = pos;
            PhysicsUtil.StripCollider(m);
            Materials.Tint(m, Color.white);
        }

        /// <summary>
        /// Simple blocks (buildings, hangars, hills) so speed and height can be judged visually.
        /// Deterministic seed: the world looks the same every launch.
        /// </summary>
        private void BuildScenery()
        {
            var root = new GameObject("Scenery");
            root.transform.SetParent(transform, false);

            Color[] palette =
            {
                new Color(0.55f, 0.55f, 0.52f), new Color(0.62f, 0.58f, 0.50f),
                new Color(0.45f, 0.47f, 0.50f), new Color(0.70f, 0.68f, 0.64f),
                new Color(0.22f, 0.33f, 0.18f), // tree clumps
            };

            // Hangars beside the runway (east side, near the threshold).
            for (int i = 0; i < 4; i++)
                Block(root.transform, $"Hangar_{i}", new Vector3(110f, 0f, -850f + i * 70f), new Vector3(40f, 12f, 50f), palette[2]);

            var rng = new System.Random(1234);
            float extent = groundSize * 0.4f;
            int placed = 0, attempts = 0;
            while (placed < sceneryBlocks && attempts < sceneryBlocks * 10)
            {
                attempts++;
                float x = Range(rng, -extent, extent);
                float z = Range(rng, -extent, extent);
                // Keep the runway and its approach/departure paths clear.
                if (Mathf.Abs(x) < 300f && Mathf.Abs(z) < runwayLength * 0.5f + 2500f) continue;

                int colorIndex = rng.Next(palette.Length);
                bool tree = colorIndex == palette.Length - 1;
                Vector3 size = tree
                    ? new Vector3(Range(rng, 15f, 40f), Range(rng, 8f, 18f), Range(rng, 15f, 40f))
                    : new Vector3(Range(rng, 10f, 40f), Range(rng, 6f, 60f), Range(rng, 10f, 40f));
                Block(root.transform, $"Block_{placed:000}", new Vector3(x, 0f, z), size, palette[colorIndex],
                    (float)rng.NextDouble() * 90f);
                placed++;
            }

            // Distant hills for a horizon reference.
            for (int i = 0; i < 14; i++)
            {
                float ang = i / 14f * Mathf.PI * 2f + 0.2f;
                float dist = Range(rng, 7000f, 9000f);
                var size = new Vector3(Range(rng, 1200f, 2500f), Range(rng, 200f, 600f), Range(rng, 600f, 1200f));
                Block(root.transform, $"Hill_{i:00}", new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist), size,
                    new Color(0.28f, 0.36f, 0.24f), ang * Mathf.Rad2Deg);
            }

            // Fewer draw calls on device: all blocks share a few materials.
            StaticBatchingUtility.Combine(root);
        }

        private static void Block(Transform parent, string blockName, Vector3 groundPos, Vector3 size, Color color, float yawDeg = 0f)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = blockName;
            b.transform.SetParent(parent, false);
            b.transform.localScale = size;
            b.transform.localPosition = groundPos + Vector3.up * (size.y * 0.5f);
            b.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            PhysicsUtil.EnsureBoxCollider(b);
            Materials.Tint(b, color);
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private void BuildCamera()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.53f, 0.71f, 0.90f); // sky blue
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 12000f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();

            _flightCamera = camGo.AddComponent<FlightCamera>();
            _flightCamera.Init(_aircraft.transform);
        }
    }
}
