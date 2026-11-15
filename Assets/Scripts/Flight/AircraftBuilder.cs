using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Builds the block aircraft (an original, generic high-wing trainer made of cubes).
    /// Origin = centre of mass. Local axes: +Z nose, +Y up, +X right wing.
    ///
    /// Colliders are kept only on the big parts (fuselage, cowling, wing, tail) and are used
    /// for crash detection; the wheels are raycasts (see LandingGear). Everything is put on
    /// the built-in "Ignore Raycast" layer so the gear rays never hit the aircraft itself.
    /// </summary>
    public static class AircraftBuilder
    {
        private const int IgnoreRaycastLayer = 2;

        /// <summary>Pilot eye position for the cockpit camera (inside the fuselage box,
        /// whose faces are back-face culled from inside, so the view is clear).</summary>
        public static readonly Vector3 CockpitEye = new Vector3(0f, 0.45f, 1.0f);

        private static readonly Color Body = new Color(0.92f, 0.92f, 0.90f);
        private static readonly Color Accent = new Color(0.80f, 0.16f, 0.12f);
        private static readonly Color Dark = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color Glass = new Color(0.15f, 0.25f, 0.38f);
        private static readonly Color Tyre = new Color(0.08f, 0.08f, 0.08f);

        public static AircraftController Build(Transform parent)
        {
            var root = new GameObject("Aircraft");
            root.transform.SetParent(parent, false);
            root.layer = IgnoreRaycastLayer;
            root.AddComponent<Rigidbody>();

            // ---------------------------------------------------- airframe (with colliders)
            Part(root.transform, "Fuselage", new Vector3(0f, 0f, -0.6f), new Vector3(1.1f, 1.2f, 7.5f), Body, true);
            Part(root.transform, "Cowling", new Vector3(0f, -0.1f, 3.65f), new Vector3(0.95f, 0.85f, 1.0f), Accent, true);
            Part(root.transform, "Wing", new Vector3(0f, 0.68f, 0.1f), new Vector3(11f, 0.15f, 1.5f), Body, true);
            Part(root.transform, "HStab", new Vector3(0f, 0.2f, -3.9f), new Vector3(3.6f, 0.1f, 1.0f), Body, true);
            Part(root.transform, "VFin", new Vector3(0f, 1.3f, -3.9f), new Vector3(0.12f, 1.5f, 1.2f), Accent, true);

            // ---------------------------------------------------- details (visual only)
            Part(root.transform, "WingTipL", new Vector3(-5.25f, 0.68f, 0.1f), new Vector3(0.52f, 0.17f, 1.52f), Accent, false);
            Part(root.transform, "WingTipR", new Vector3(5.25f, 0.68f, 0.1f), new Vector3(0.52f, 0.17f, 1.52f), Accent, false);
            Part(root.transform, "Windows", new Vector3(0f, 0.3f, 0.6f), new Vector3(1.12f, 0.45f, 1.4f), Glass, false);
            Part(root.transform, "Windshield", new Vector3(0f, 0.42f, 1.32f), new Vector3(1.0f, 0.35f, 0.05f), Glass, false);
            Part(root.transform, "Stripe", new Vector3(0f, -0.1f, -0.6f), new Vector3(1.12f, 0.12f, 7.4f), Accent, false);
            Part(root.transform, "StrutL", new Vector3(-1.4f, 0.05f, 0.2f), new Vector3(2.4f, 0.06f, 0.12f), Body, false,
                Quaternion.Euler(0f, 0f, -28f));
            Part(root.transform, "StrutR", new Vector3(1.4f, 0.05f, 0.2f), new Vector3(2.4f, 0.06f, 0.12f), Body, false,
                Quaternion.Euler(0f, 0f, 28f));
            // Instrument panel seen from the cockpit camera.
            Part(root.transform, "Panel", new Vector3(0f, 0.05f, 1.9f), new Vector3(1.0f, 0.45f, 0.1f), Dark, false);

            // Propeller: a pivot that AircraftController spins around local Z.
            var prop = new GameObject("Propeller");
            prop.layer = IgnoreRaycastLayer;
            prop.transform.SetParent(root.transform, false);
            prop.transform.localPosition = new Vector3(0f, -0.1f, 4.22f);
            Part(prop.transform, "Spinner", Vector3.zero, new Vector3(0.28f, 0.28f, 0.3f), Body, false);
            Part(prop.transform, "Blade", new Vector3(0f, 0f, 0.05f), new Vector3(1.8f, 0.14f, 0.04f), Dark, false);

            // ---------------------------------------------------- landing gear
            var gear = root.AddComponent<LandingGear>();
            AddGear(root.transform, gear, "NoseGear", new Vector3(0f, -0.6f, 2.6f), steerable: true, braked: false, isNose: true);
            AddGear(root.transform, gear, "MainGearL", new Vector3(-1.3f, -0.6f, -0.3f), steerable: false, braked: true, isNose: false);
            AddGear(root.transform, gear, "MainGearR", new Vector3(1.3f, -0.6f, -0.3f), steerable: false, braked: true, isNose: false);

            // Added last so its Awake sees the finished Rigidbody and gear.
            var controller = root.AddComponent<AircraftController>();
            controller.Propeller = prop.transform;
            return controller;
        }

        private static void AddGear(Transform root, LandingGear gear, string gearName, Vector3 attach,
            bool steerable, bool braked, bool isNose)
        {
            float rest = gear.RestLength;
            float radius = gear.WheelRadius;

            // Static strut leg from the fuselage down to the wheel (visual only).
            Part(root, gearName + "Leg", attach + new Vector3(0f, -rest * 0.45f, 0f), new Vector3(0.08f, rest * 0.9f, 0.08f), Dark, false);

            // Wheel: a block (cubes only, see PhysicsUtil). Its position follows the suspension.
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wheel.name = gearName + "Wheel";
            wheel.layer = IgnoreRaycastLayer;
            PhysicsUtil.StripCollider(wheel);
            wheel.transform.SetParent(root, false);
            wheel.transform.localPosition = attach + Vector3.down * rest;
            wheel.transform.localRotation = Quaternion.identity;
            wheel.transform.localScale = new Vector3(0.2f, radius * 2f, radius * 2f);
            Materials.Tint(wheel, Tyre);

            gear.AddWheel(gearName, attach, steerable, braked, isNose, wheel.transform);
        }

        private static GameObject Part(Transform parent, string partName, Vector3 localPos, Vector3 size, Color color,
            bool keepCollider, Quaternion? localRot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            go.layer = IgnoreRaycastLayer;
            if (keepCollider) PhysicsUtil.EnsureBoxCollider(go);
            else PhysicsUtil.StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = size;
            Materials.Tint(go, color);
            return go;
        }
    }
}
