using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// 2D HUD drawn with IMGUI: airspeed, altitude, vertical speed, heading, throttle/flaps/trim,
    /// an attitude indicator (horizon + pitch ladder + bank), stall warning and landing results.
    /// Units follow aviation convention: knots, feet, feet per minute.
    /// </summary>
    public sealed class FlightHud : MonoBehaviour
    {
        private AircraftController _ac;
        private LandingJudge _judge;
        private FlightInput _input;
        private float _fps = 60f;

        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.4f);
        private static readonly Color Label = new Color(1f, 1f, 1f, 0.7f);
        private static readonly Color Symbol = new Color(1f, 0.6f, 0.1f);
        private static readonly Color Warn = new Color(1f, 0.3f, 0.25f);

        public void Init(AircraftController aircraft, LandingJudge judge, FlightInput input)
        {
            _ac = aircraft;
            _judge = judge;
            _input = input;
        }

        private void Update()
        {
            float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _fps = Mathf.Lerp(_fps, fps, 0.05f);
        }

        private void OnGUI()
        {
            if (_ac == null || Event.current.type != EventType.Repaint) return;
            GuiDraw.UpdateLayout();
            Rect sa = GuiDraw.Safe;

            DrawDataBoxes(sa);
            DrawAttitude(sa);
            DrawBanner(sa);
            DrawHints(sa);
        }

        private void DrawDataBoxes(Rect sa)
        {
            float u = GuiDraw.S;
            float x = sa.x + 12f * u, y = sa.y + 12f * u;
            float w = 136f * u, h = 64f * u, gap = 8f * u;

            Box(new Rect(x, y, w, h), "IAS  kt", $"{_ac.AirspeedKts:0}");
            Box(new Rect(x + (w + gap), y, w, h), "ALT  ft", $"{_ac.AltitudeFt:0}");
            Box(new Rect(x + 2f * (w + gap), y, w, h), "V/S  fpm", $"{Mathf.Round(_ac.VerticalSpeedFpm / 10f) * 10f:+0;-0;0}");
            Box(new Rect(x + 3f * (w + gap), y, w, h), "HDG", $"{Mathf.Round(_ac.HeadingDeg) % 360f:000}");

            string line = $"AGL {_ac.AltitudeAglM * 3.28084f:0} ft   THR {_input.Throttle * 100f:0}%   " +
                          $"FLAPS {_ac.FlapDeg}°   TRIM {_ac.PitchTrim:+0.00;-0.00;0.00}   {_fps:0} FPS";
            GuiDraw.Text(new Rect(x, y + h + 4f * u, 4f * w + 3f * gap, 24f * u), line, 15f, Color.white, TextAnchor.MiddleLeft, false);

            if (_judge != null && _judge.HasReport)
            {
                var r = _judge.Report;
                GuiDraw.Text(new Rect(x, y + h + 28f * u, 4f * w + 3f * gap, 24f * u),
                    $"LAST TOUCHDOWN  {r.Fpm:0} fpm  {r.Rating}", 15f, r.RatingColor, TextAnchor.MiddleLeft);
            }
        }

        private static void Box(Rect r, string label, string value)
        {
            float u = GuiDraw.S;
            GuiDraw.Fill(r, Panel);
            GuiDraw.Text(new Rect(r.x, r.y + 4f * u, r.width, 18f * u), label, 13f, Label, TextAnchor.MiddleCenter, false);
            GuiDraw.Text(new Rect(r.x, r.y + 20f * u, r.width, r.height - 22f * u), value, 30f, Color.white);
        }

        private void DrawAttitude(Rect sa)
        {
            float u = GuiDraw.S;
            float half = 100f * u;
            Vector2 c = new Vector2(sa.x + sa.width * 0.40f, sa.yMax - 140f * u);
            Rect box = new Rect(c.x - half, c.y - half, 2f * half, 2f * half);
            GuiDraw.Fill(box, Panel);
            GuiDraw.Frame(box, Mathf.Max(1f, 2f * u), new Color(1f, 1f, 1f, 0.5f));

            float pitch = _ac.PitchDeg;
            float bank = _ac.BankDeg;
            float pxPerDeg = 4f * u;

            // Banking right makes the horizon rotate counter-clockwise on screen.
            float a = -bank * Mathf.Deg2Rad;
            Vector2 along = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 down = new Vector2(-along.y, along.x); // towards the ground side

            // Horizon (nose up -> horizon moves down)
            Vector2 hc = c + down * (pitch * pxPerDeg);
            if (Mathf.Abs(pitch * pxPerDeg) < half * 0.95f)
            {
                float hl = half * 0.9f;
                GuiDraw.Line(hc - along * hl, hc + along * hl, 3f * u, Color.white);
                // Ground-side ticks so it is obvious which side is down.
                for (int i = -3; i <= 3; i++)
                {
                    Vector2 p = hc + along * (i * hl / 3.2f);
                    GuiDraw.Line(p, p + down * 10f * u, 2f * u, new Color(0.75f, 0.5f, 0.25f));
                }
            }

            // Pitch ladder
            for (int deg = -30; deg <= 30; deg += 5)
            {
                if (deg == 0) continue;
                float off = (pitch - deg) * pxPerDeg;
                if (Mathf.Abs(off) > half * 0.8f) continue;
                Vector2 lc = c + down * off;
                float len = (deg % 10 == 0 ? 34f : 18f) * u;
                GuiDraw.Line(lc - along * len, lc + along * len, 2f * u, new Color(1f, 1f, 1f, 0.8f));
                if (deg % 10 == 0)
                {
                    Vector2 tp = lc + along * (len + 16f * u);
                    GuiDraw.Text(new Rect(tp.x - 14f * u, tp.y - 9f * u, 28f * u, 18f * u), Mathf.Abs(deg).ToString(), 12f, Color.white);
                }
            }

            // Bank pointer on a fixed scale at the top
            float rr = half * 0.92f;
            foreach (int t in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
            {
                float ta = (t - 90f) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
                float tl = (t % 30 == 0 ? 12f : 7f) * u;
                GuiDraw.Line(c + dir * rr, c + dir * (rr - tl), 2f * u, new Color(1f, 1f, 1f, 0.7f));
            }
            float pa = (-bank - 90f) * Mathf.Deg2Rad;
            Vector2 pdir = new Vector2(Mathf.Cos(pa), Mathf.Sin(pa));
            GuiDraw.Line(c + pdir * (rr - 14f * u), c + pdir * (rr - 30f * u), 5f * u, Symbol);

            // Fixed aircraft symbol
            float s = 26f * u;
            GuiDraw.Line(c + new Vector2(-3f * s, 0f), c + new Vector2(-s, 0f), 4f * u, Symbol);
            GuiDraw.Line(c + new Vector2(-s, 0f), c + new Vector2(-s, 0.5f * s), 4f * u, Symbol);
            GuiDraw.Line(c + new Vector2(s, 0f), c + new Vector2(3f * s, 0f), 4f * u, Symbol);
            GuiDraw.Line(c + new Vector2(s, 0f), c + new Vector2(s, 0.5f * s), 4f * u, Symbol);
            GuiDraw.Fill(new Rect(c.x - 3f * u, c.y - 3f * u, 6f * u, 6f * u), Symbol);

            GuiDraw.Text(new Rect(box.x, box.yMax + 2f * u, box.width, 22f * u),
                $"PITCH {pitch:+0;-0;0}°   BANK {bank:+0;-0;0}°   AOA {_ac.AlphaDeg:0}°", 14f, Color.white, TextAnchor.MiddleCenter, false);

            if (_ac.StallWarning && Mathf.Repeat(Time.time, 0.5f) < 0.3f)
                GuiDraw.Text(new Rect(box.x - 40f * u, box.y - 46f * u, box.width + 80f * u, 40f * u), "STALL", 34f, Warn);
        }

        private void DrawBanner(Rect sa)
        {
            if (_judge == null || string.IsNullOrEmpty(_judge.Banner) || Time.time > _judge.BannerUntil) return;
            float u = GuiDraw.S;
            float w = Mathf.Min(sa.width - 40f * u, 760f * u);
            Rect r = new Rect(sa.x + (sa.width - w) * 0.5f, sa.y + 128f * u, w, 104f * u);
            GuiDraw.Fill(r, new Color(0f, 0f, 0f, 0.5f));
            GuiDraw.Text(new Rect(r.x, r.y + 6f * u, r.width, 54f * u), _judge.Banner, 42f, _judge.BannerColor);
            GuiDraw.Text(new Rect(r.x + 10f * u, r.y + 62f * u, r.width - 20f * u, 34f * u), _judge.SubBanner ?? "", 16f, Color.white, TextAnchor.MiddleCenter, false);
        }

        private void DrawHints(Rect sa)
        {
            if (_judge == null || _judge.Phase != FlightPhase.Parked || _ac.GroundSpeedMs > 2f) return;
            if (!string.IsNullOrEmpty(_judge.Banner) && Time.time <= _judge.BannerUntil) return;
            float u = GuiDraw.S;
            float w = Mathf.Min(sa.width - 40f * u, 760f * u);
            Rect r = new Rect(sa.x + (sa.width - w) * 0.5f, sa.y + 128f * u, w, 70f * u);
            GuiDraw.Fill(r, new Color(0f, 0f, 0f, 0.35f));
            GuiDraw.Text(new Rect(r.x, r.y + 4f * u, r.width, 30f * u),
                "Throttle up, keep straight, pull back gently at 55 kt", 18f, Color.white);
            GuiDraw.Text(new Rect(r.x, r.y + 36f * u, r.width, 26f * u),
                "Land: ~65 kt, flaps 30, sink under 300 fpm. APPROACH = start on final", 15f, Label, TextAnchor.MiddleCenter, false);
        }
    }
}
