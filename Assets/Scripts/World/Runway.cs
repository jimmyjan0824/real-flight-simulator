using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Runway geometry. The runway is aligned with world +Z (heading 360):
    /// you take off and land towards +Z, starting from the threshold at the -Z end.
    /// </summary>
    public sealed class Runway
    {
        public Vector3 Center;
        public float Length;
        public float Width;
        public float SurfaceY;

        public Runway(Vector3 center, float length, float width, float surfaceY)
        {
            Center = center;
            Length = length;
            Width = width;
            SurfaceY = surfaceY;
        }

        /// <summary>Landing threshold (start of the usable runway) on the centreline.</summary>
        public Vector3 Threshold => new Vector3(Center.x, SurfaceY, Center.z - Length * 0.5f);

        public float DistanceFromThreshold(Vector3 worldPos) => worldPos.z - (Center.z - Length * 0.5f);

        /// <summary>Positive = right of the centreline when facing the landing direction.</summary>
        public float CenterlineOffset(Vector3 worldPos) => worldPos.x - Center.x;

        public bool Contains(Vector3 worldPos) =>
            Mathf.Abs(CenterlineOffset(worldPos)) <= Width * 0.5f &&
            Mathf.Abs(worldPos.z - Center.z) <= Length * 0.5f;
    }
}
