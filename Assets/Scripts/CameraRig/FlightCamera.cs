using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Two views: a smoothed third-person chase camera and a fixed cockpit (nose) view.
    /// Runs in LateUpdate, after the interpolated Rigidbody has moved the aircraft.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FlightCamera : MonoBehaviour
    {
        public enum ViewMode { Chase, Cockpit }

        [SerializeField] private float chaseDistance = 16f;
        [SerializeField] private float chaseHeight = 4.5f;
        [SerializeField] private float lookAhead = 12f;
        [SerializeField] private float smoothTime = 0.12f;
        [SerializeField] private float cockpitLookDownDeg = 8f;

        public ViewMode Mode { get; private set; } = ViewMode.Chase;

        private Camera _cam;
        private Transform _target;
        private Vector3 _velocity;

        public void Init(Transform target)
        {
            _cam = GetComponent<Camera>();
            _target = target;
            ApplyLens();
            Snap();
        }

        public void Toggle()
        {
            Mode = Mode == ViewMode.Chase ? ViewMode.Cockpit : ViewMode.Chase;
            ApplyLens();
            Snap();
        }

        /// <summary>Jump straight to the desired pose (after a reset or view change).</summary>
        public void Snap()
        {
            _velocity = Vector3.zero;
            UpdatePose(true);
        }

        private void ApplyLens()
        {
            if (_cam == null) return;
            bool cockpit = Mode == ViewMode.Cockpit;
            _cam.nearClipPlane = cockpit ? 0.05f : 0.3f;
            _cam.fieldOfView = cockpit ? 70f : 60f;
        }

        private void LateUpdate() => UpdatePose(false);

        private void UpdatePose(bool snap)
        {
            if (_target == null) return;

            if (Mode == ViewMode.Cockpit)
            {
                transform.SetPositionAndRotation(
                    _target.TransformPoint(AircraftBuilder.CockpitEye),
                    _target.rotation * Quaternion.Euler(cockpitLookDownDeg, 0f, 0f));
                return;
            }

            Vector3 fwd = _target.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(_target.up, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            // Follow the heading fully and the pitch halfway, so climbs/descents stay readable.
            Vector3 back = -Vector3.Slerp(flat, fwd, 0.5f).normalized;
            Vector3 desired = _target.position + back * chaseDistance + Vector3.up * chaseHeight;
            desired.y = Mathf.Max(desired.y, 1.0f);

            transform.position = snap
                ? desired
                : Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime);

            Vector3 look = _target.position + fwd * lookAhead;
            Vector3 up = Vector3.Slerp(Vector3.up, _target.up, 0.25f);
            Vector3 dir = look - transform.position;
            if (dir.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(dir, up);
        }
    }
}
