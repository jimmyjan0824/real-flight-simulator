using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// iOS builds use "Strip Engine Code": native engine classes that are never referenced
    /// from a scene or from C# are removed. GameObject.CreatePrimitive() adds its collider
    /// natively, so without an explicit C# reference the BoxCollider class is stripped and
    /// primitives silently get no collider on device: the aircraft falls through the runway
    /// and every raycast misses (it still works in the Editor, which never strips).
    ///
    /// Rule for this project: only build from cubes, and always go through
    /// EnsureBoxCollider / StripCollider so BoxCollider stays referenced. Assets/link.xml
    /// additionally preserves the physics module.
    /// </summary>
    public static class PhysicsUtil
    {
        public static BoxCollider EnsureBoxCollider(GameObject go)
        {
            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            return box;
        }

        public static void StripCollider(GameObject go)
        {
            // DestroyImmediate so the collider never takes part in a physics step
            // (Destroy is deferred to the end of the frame).
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }

        /// <summary>Logs an error if there is no ground under the given point.</summary>
        public static void CheckGround(Vector3 from)
        {
            if (!Physics.Raycast(from + Vector3.up * 10f, Vector3.down, 100f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Debug.LogError("[FlightSim] No ground collider under the aircraft. Colliders were stripped from the build?");
            else
                Debug.Log("[FlightSim] Ground collider OK.");
        }
    }
}
