using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Code-first entry point. The main scene contains only one GameObject with this
    /// component; everything else (camera, light, ground, runway) is built at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrapper : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] private float groundSize = 6000f;      // metres
        [SerializeField] private float runwayLength = 2000f;    // metres
        [SerializeField] private float runwayWidth = 45f;       // metres

        [Header("Camera")]
        [SerializeField] private float orbitRadius = 120f;
        [SerializeField] private float orbitHeight = 35f;
        [SerializeField] private float orbitSpeedDegPerSec = 6f;

        private Camera _camera;
        private Transform _cameraTarget;
        private float _orbitAngle;
        private float _fpsSmoothed;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            BuildLighting();
            BuildGround();
            BuildRunway();
            BuildCamera();
        }

        private void Update()
        {
            _orbitAngle += orbitSpeedDegPerSec * Time.deltaTime;
            float rad = _orbitAngle * Mathf.Deg2Rad;
            Vector3 center = _cameraTarget.position;
            _camera.transform.position = center + new Vector3(Mathf.Sin(rad) * orbitRadius, orbitHeight, Mathf.Cos(rad) * orbitRadius);
            _camera.transform.LookAt(center);

            float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, fps, 0.05f);
        }

        private void OnGUI()
        {
            // Minimal on-screen proof that the build is running on device.
            float scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * scale) };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(20 * scale, 20 * scale, 600 * scale, 40 * scale),
                $"Flight Sim MVP – Stage 0   {_fpsSmoothed:0} FPS", style);
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
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(transform, false);
            // Unity's Plane primitive is 10 x 10 m at scale 1.
            ground.transform.localScale = new Vector3(groundSize / 10f, 1f, groundSize / 10f);
            Tint(ground, new Color(0.30f, 0.42f, 0.22f));
        }

        private void BuildRunway()
        {
            var runway = new GameObject("Runway");
            runway.transform.SetParent(transform, false);
            _cameraTarget = runway.transform;

            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "Surface";
            surface.transform.SetParent(runway.transform, false);
            surface.transform.localScale = new Vector3(runwayWidth, 0.1f, runwayLength);
            surface.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            Tint(surface, new Color(0.18f, 0.18f, 0.19f));

            // Dashed centreline: 30 m stripes with 20 m gaps.
            const float stripeLen = 30f, gap = 20f;
            float start = -runwayLength / 2f + 60f;
            float end = runwayLength / 2f - 60f;
            int i = 0;
            for (float z = start; z + stripeLen <= end; z += stripeLen + gap, i++)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Centreline_{i:00}";
                stripe.transform.SetParent(runway.transform, false);
                stripe.transform.localScale = new Vector3(0.9f, 0.02f, stripeLen);
                stripe.transform.localPosition = new Vector3(0f, 0.11f, z + stripeLen / 2f);
                Destroy(stripe.GetComponent<Collider>());
                Tint(stripe, Color.white);
            }
        }

        private void BuildCamera()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.53f, 0.71f, 0.90f); // sky blue
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 10000f;
            _camera.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
        }

        /// <summary>
        /// Clone the primitive's default material (whose shader is always included in builds)
        /// instead of Shader.Find, which can return a shader stripped from the player build.
        /// </summary>
        private static void Tint(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            var mat = new Material(r.sharedMaterial) { color = color };
            r.sharedMaterial = mat;
        }
    }
}
