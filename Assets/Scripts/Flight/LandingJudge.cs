using UnityEngine;

namespace FlightSim
{
    public enum FlightPhase
    {
        Parked,    // on the ground, not yet flown
        Airborne,
        Rollout,   // touched down, still rolling
        Stopped,   // landed and came to a stop
        Crashed,
    }

    public struct TouchdownReport
    {
        public float Fpm;                 // sink rate at the moment of touchdown, ft/min (positive = down)
        public float GroundSpeedKts;
        public float DistanceFromThresholdM;
        public float CenterlineOffsetM;
        public float BankDeg;
        public bool OnRunway;
        public bool NoseFirst;
        public int Bounces;
        public string Rating;
        public Color RatingColor;
    }

    /// <summary>
    /// Watches the gear and decides: take-off, touchdown (with sink rate in FPM), bounces,
    /// full stop, and crashes (wing/prop strike, hard impact, gear collapse).
    ///
    /// The touchdown sink rate is the vertical speed from the last physics step before any
    /// wheel touched, i.e. before the suspension starts slowing the aircraft down.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class LandingJudge : MonoBehaviour
    {
        public const float CrashFpm = 800f;

        private const float MsToFpm = 196.8504f;
        private const float MsToKts = 1.943844f;

        public FlightPhase Phase { get; private set; } = FlightPhase.Parked;
        public bool HasReport { get; private set; }
        public TouchdownReport Report => _report;

        public string Banner { get; private set; }
        public string SubBanner { get; private set; }
        public Color BannerColor { get; private set; } = Color.white;
        public float BannerUntil { get; private set; }

        private AircraftController _ac;
        private Runway _runway;
        private TouchdownReport _report;
        private float _airTime;
        private float _lastAirVy;
        private bool _bounceWindow;

        public void Init(AircraftController ac, Runway runway)
        {
            _ac = ac;
            _runway = runway;
        }

        public void ResetState(bool airborne)
        {
            Phase = airborne ? FlightPhase.Airborne : FlightPhase.Parked;
            HasReport = false;
            _report = default;
            _airTime = airborne ? 10f : 0f;
            _lastAirVy = 0f;
            _bounceWindow = false;
            Banner = null;
        }

        private void FixedUpdate()
        {
            if (_ac == null || Phase == FlightPhase.Crashed) return;

            float dt = Time.fixedDeltaTime;
            var gear = _ac.Gear;

            if (gear.GroundedCount == 0)
            {
                _airTime += dt;
                _lastAirVy = _ac.Body.linearVelocity.y;

                switch (Phase)
                {
                    case FlightPhase.Parked:
                    case FlightPhase.Stopped:
                        if (_airTime > 1f && _ac.AltitudeAglM > 3f)
                        {
                            Phase = FlightPhase.Airborne;
                            HasReport = false;
                            ShowBanner("TAKEOFF", "Climb, fly a circuit, then land on the runway", Color.white, 3f);
                        }
                        break;

                    case FlightPhase.Rollout:
                        // Left the ground again shortly after touching down.
                        if (_airTime > 0.2f)
                        {
                            Phase = FlightPhase.Airborne;
                            _bounceWindow = true;
                        }
                        break;

                    case FlightPhase.Airborne:
                        // Airborne for a while after a touchdown: that was a touch-and-go,
                        // so the next touchdown is a new landing, not a bounce.
                        if (_bounceWindow && _airTime > 4f) _bounceWindow = false;
                        break;
                }
                return;
            }

            if (Phase == FlightPhase.Airborne)
                OnTouchdown(gear);

            _airTime = 0f;

            if (Phase == FlightPhase.Rollout && _ac.GroundSpeedMs < 1f)
            {
                Phase = FlightPhase.Stopped;
                ShowBanner("LANDED", Summary(), _report.RatingColor, 8f);
            }
        }

        private void OnTouchdown(LandingGear gear)
        {
            float fpm = -_lastAirVy * MsToFpm;
            Vector3 pos = _ac.transform.position;

            if (_bounceWindow && HasReport)
            {
                _bounceWindow = false;
                _report.Bounces++;
                if (fpm > _report.Fpm) Rate(ref _report, fpm);
                if (fpm >= CrashFpm) { Crash("GEAR COLLAPSED", $"Bounced and hit at {fpm:0} fpm"); return; }
                Phase = FlightPhase.Rollout;
                ShowBanner($"BOUNCE x{_report.Bounces}", $"{fpm:0} fpm", new Color(1f, 0.75f, 0.2f), 3f);
                return;
            }

            _bounceWindow = false;
            _report = new TouchdownReport
            {
                GroundSpeedKts = _ac.GroundSpeedMs * MsToKts,
                DistanceFromThresholdM = _runway != null ? _runway.DistanceFromThreshold(pos) : 0f,
                CenterlineOffsetM = _runway != null ? _runway.CenterlineOffset(pos) : 0f,
                BankDeg = _ac.BankDeg,
                OnRunway = _runway == null || _runway.Contains(pos),
                NoseFirst = gear.NoseGrounded && gear.MainGroundedCount == 0,
            };
            Rate(ref _report, fpm);
            HasReport = true;

            if (fpm >= CrashFpm)
            {
                Crash("GEAR COLLAPSED", $"Touchdown at {fpm:0} fpm (limit {CrashFpm:0})");
                return;
            }

            Phase = FlightPhase.Rollout;
            string sub = $"{fpm:0} fpm";
            if (_report.NoseFirst) sub += "  |  NOSE WHEEL FIRST";
            if (Mathf.Abs(_report.BankDeg) > 8f) sub += "  |  WING LOW";
            if (!_report.OnRunway) sub += "  |  OFF RUNWAY";
            ShowBanner(_report.Rating, sub, _report.RatingColor, 5f);
        }

        private static void Rate(ref TouchdownReport r, float fpm)
        {
            r.Fpm = fpm;
            if (fpm < 120f) { r.Rating = "BUTTER!"; r.RatingColor = new Color(0.4f, 1f, 0.6f); }
            else if (fpm < 300f) { r.Rating = "SMOOTH"; r.RatingColor = new Color(0.55f, 0.9f, 1f); }
            else if (fpm < 500f) { r.Rating = "FIRM"; r.RatingColor = Color.white; }
            else if (fpm < CrashFpm) { r.Rating = "HARD"; r.RatingColor = new Color(1f, 0.65f, 0.2f); }
            else { r.Rating = "CRASH"; r.RatingColor = new Color(1f, 0.3f, 0.25f); }
        }

        private string Summary()
        {
            var r = _report;
            string side = r.CenterlineOffsetM >= 0f ? "R" : "L";
            string s = $"{r.Rating} {r.Fpm:0} fpm  |  {r.GroundSpeedKts:0} kt  |  " +
                       $"{r.DistanceFromThresholdM:0} m past threshold  |  {Mathf.Abs(r.CenterlineOffsetM):0.0} m {side} of centreline";
            if (r.Bounces > 0) s += $"  |  {r.Bounces} bounce(s)";
            if (!r.OnRunway) s += "  |  OFF RUNWAY";
            return s;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_ac == null || Phase == FlightPhase.Crashed) return;

            Collider part = collision.contactCount > 0 ? collision.GetContact(0).thisCollider : null;
            string partName = part != null ? part.name : "Airframe";
            float impact = collision.relativeVelocity.magnitude;

            switch (partName)
            {
                case "Wing":
                    Crash("WING STRIKE", $"Impact {impact:0.0} m/s");
                    break;
                case "Cowling":
                    Crash("PROP STRIKE", $"Impact {impact:0.0} m/s");
                    break;
                default:
                    if (impact > 5f)
                        Crash("CRASHED", $"{partName} hit at {impact:0.0} m/s");
                    else
                        ShowBanner("TAIL STRIKE", "Rotate less steeply", new Color(1f, 0.65f, 0.2f), 3f);
                    break;
            }
        }

        private void Crash(string title, string detail)
        {
            Phase = FlightPhase.Crashed;
            _ac.EngineRunning = false;
            _ac.Brake = 1f;
            ShowBanner(title, detail + "  -  tap RESET", new Color(1f, 0.3f, 0.25f), float.PositiveInfinity);
        }

        private void ShowBanner(string title, string sub, Color color, float seconds)
        {
            Banner = title;
            SubBanner = sub;
            BannerColor = color;
            BannerUntil = Time.time + seconds;
        }
    }
}
