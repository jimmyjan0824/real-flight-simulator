using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlightSim
{
    public enum ControlMode { Touch, Tilt }

    /// <summary>
    /// Mobile flight controls (legacy Input Manager; the project's Active Input Handling is
    /// "Input Manager (Old)").
    ///
    ///   Left edge      throttle slider (drag up/down)
    ///   Left column    TRIM UP / TRIM DN (hold), FLAPS (tap to cycle), BRAKE (hold)
    ///   Right side     floating virtual stick (Touch mode): drag left/right = roll,
    ///                  drag down = pull (nose up), drag up = push (nose down)
    ///   Tilt mode      steer the phone like a wheel to roll, tilt the top edge
    ///                  towards you to pull up. CENTER re-calibrates the neutral position.
    ///   Top right      CTRL (touch/tilt), VIEW, APPROACH (start on final), RESET
    ///
    /// On the ground the stick's left/right also steers the nose wheel; in the air a little
    /// rudder is mixed in automatically so turns stay coordinated.
    ///
    /// Keyboard (Unity Editor / iPad keyboard): W/S or Up/Down pitch, A/D or Left/Right roll,
    /// Q/E rudder, Shift/Ctrl throttle, F flaps, B brake, [ ] trim, V view, M control mode,
    /// R reset, T approach.
    /// </summary>
    public sealed class FlightInput : MonoBehaviour
    {
        public event Action ResetRunwayRequested;
        public event Action ResetApproachRequested;
        public event Action ViewToggleRequested;

        public ControlMode Mode { get; private set; } = ControlMode.Touch;

        [SerializeField] private float expo = 0.45f;
        [SerializeField] private float autoRudder = 0.2f;
        [SerializeField] private float stickRadiusUnits = 110f;
        [SerializeField] private float tiltRollRangeDeg = 35f;
        [SerializeField] private float tiltPitchRangeDeg = 25f;
        [SerializeField] private float tiltDeadzoneDeg = 2f;
        [SerializeField] private float trimRatePerSec = 0.2f;

        private enum Btn { Reset, Approach, View, Mode, Calibrate, TrimUp, TrimDown, Flaps, Brake }
        private static readonly Btn[] Buttons = (Btn[])Enum.GetValues(typeof(Btn));

        private struct Pointer
        {
            public int Id;
            public Vector2 Pos;      // GUI coordinates
            public TouchPhase Phase;
        }

        private const int NoFinger = int.MinValue;
        private const int MouseId = -100;

        private readonly List<Pointer> _pointers = new List<Pointer>(10);
        private AircraftController _ac;

        private int _stickFinger = NoFinger, _throttleFinger = NoFinger, _brakeFinger = NoFinger, _trimFinger = NoFinger;
        private int _trimDir;
        private Vector2 _stickOrigin, _stickPos;
        private float _touchRoll, _touchPitch;
        private float _throttle;

        private Vector3 _acc;
        private bool _accValid;
        private float _tiltRollZero, _tiltPitchZero;
        private float _tiltRoll, _tiltPitch;

        public float Throttle => _throttle;

        public void Init(AircraftController aircraft)
        {
            _ac = aircraft;
            Input.multiTouchEnabled = true;
            if (!Application.isEditor) Input.simulateMouseWithTouches = false;
        }

        public void SetThrottle(float t) => _throttle = Mathf.Clamp01(t);

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (_ac == null) return;
            GuiDraw.UpdateLayout();
            float dt = Time.deltaTime;

            CollectPointers();
            foreach (var p in _pointers) HandlePointer(p);

            HandleKeyboard(dt, out float kbPitch, out float kbRoll, out float kbYaw);

            if (_trimFinger != NoFinger) AdjustTrim(_trimDir * trimRatePerSec * dt);

            UpdateTilt(dt);

            float pitch = Mode == ControlMode.Touch ? _touchPitch : _tiltPitch;
            float roll = Mode == ControlMode.Touch ? _touchRoll : _tiltRoll;
            pitch = Expo(Mathf.Clamp(pitch + kbPitch, -1f, 1f));
            roll = Expo(Mathf.Clamp(roll + kbRoll, -1f, 1f));

            bool onGround = _ac.Gear.AnyGrounded;
            float yaw = kbYaw + (onGround ? roll : roll * autoRudder);

            _ac.Pitch = pitch;
            _ac.Roll = roll;
            _ac.Yaw = Mathf.Clamp(yaw, -1f, 1f);
            _ac.Throttle = _throttle;
            _ac.Brake = (_brakeFinger != NoFinger || Input.GetKey(KeyCode.B)) ? 1f : 0f;
        }

        private float Expo(float x) => x * (1f - expo) + x * x * x * expo;

        private void AdjustTrim(float delta)
        {
            _ac.PitchTrim = Mathf.Clamp(_ac.PitchTrim + delta, -AircraftController.TrimLimit, AircraftController.TrimLimit);
        }

        private void CollectPointers()
        {
            _pointers.Clear();
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                _pointers.Add(new Pointer { Id = t.fingerId, Pos = GuiDraw.ScreenToGui(t.position), Phase = t.phase });
            }

            // Mouse acts as a single finger in the Editor.
            if (Application.isEditor && Input.touchCount == 0)
            {
                Vector2 pos = GuiDraw.ScreenToGui(Input.mousePosition);
                if (Input.GetMouseButtonDown(0)) _pointers.Add(new Pointer { Id = MouseId, Pos = pos, Phase = TouchPhase.Began });
                else if (Input.GetMouseButton(0)) _pointers.Add(new Pointer { Id = MouseId, Pos = pos, Phase = TouchPhase.Moved });
                else if (Input.GetMouseButtonUp(0)) _pointers.Add(new Pointer { Id = MouseId, Pos = pos, Phase = TouchPhase.Ended });
            }
        }

        private void HandlePointer(Pointer p)
        {
            switch (p.Phase)
            {
                case TouchPhase.Began:
                    foreach (var b in Buttons)
                    {
                        if (ButtonVisible(b) && ButtonRect(b).Contains(p.Pos))
                        {
                            PressButton(b, p.Id);
                            return;
                        }
                    }
                    Rect thr = ThrottleRect();
                    Rect thrHit = new Rect(thr.x - GuiDraw.U(16), thr.y - GuiDraw.U(30), thr.width + GuiDraw.U(40), thr.height + GuiDraw.U(60));
                    if (thrHit.Contains(p.Pos))
                    {
                        _throttleFinger = p.Id;
                        SetThrottleFromY(p.Pos.y);
                        return;
                    }
                    if (Mode == ControlMode.Touch && _stickFinger == NoFinger && StickZone().Contains(p.Pos))
                    {
                        _stickFinger = p.Id;
                        _stickOrigin = p.Pos;
                        _stickPos = p.Pos;
                        UpdateStick();
                    }
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (p.Id == _throttleFinger) SetThrottleFromY(p.Pos.y);
                    else if (p.Id == _stickFinger) { _stickPos = p.Pos; UpdateStick(); }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    Release(p.Id);
                    break;
            }
        }

        private void Release(int id)
        {
            if (id == _stickFinger) { _stickFinger = NoFinger; _touchRoll = 0f; _touchPitch = 0f; }
            if (id == _throttleFinger) _throttleFinger = NoFinger;
            if (id == _brakeFinger) _brakeFinger = NoFinger;
            if (id == _trimFinger) _trimFinger = NoFinger;
        }

        private void ReleaseAll()
        {
            _stickFinger = _throttleFinger = _brakeFinger = _trimFinger = NoFinger;
            _touchRoll = _touchPitch = 0f;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) ReleaseAll();
        }

        private void PressButton(Btn b, int fingerId)
        {
            switch (b)
            {
                case Btn.Reset: ResetRunwayRequested?.Invoke(); break;
                case Btn.Approach: ResetApproachRequested?.Invoke(); break;
                case Btn.View: ViewToggleRequested?.Invoke(); break;
                case Btn.Mode: ToggleMode(); break;
                case Btn.Calibrate: CalibrateTilt(); break;
                case Btn.TrimUp: _trimFinger = fingerId; _trimDir = 1; AdjustTrim(0.01f); break;
                case Btn.TrimDown: _trimFinger = fingerId; _trimDir = -1; AdjustTrim(-0.01f); break;
                case Btn.Flaps: _ac.CycleFlaps(); break;
                case Btn.Brake: _brakeFinger = fingerId; break;
            }
        }

        private void ToggleMode()
        {
            ReleaseAll();
            Mode = Mode == ControlMode.Touch ? ControlMode.Tilt : ControlMode.Touch;
            if (Mode == ControlMode.Tilt) CalibrateTilt();
        }

        private void SetThrottleFromY(float guiY)
        {
            Rect r = ThrottleRect();
            _throttle = Mathf.Clamp01(1f - (guiY - r.y) / r.height);
        }

        private void UpdateStick()
        {
            float radius = GuiDraw.U(stickRadiusUnits);
            Vector2 d = _stickPos - _stickOrigin;
            _touchRoll = Mathf.Clamp(d.x / radius, -1f, 1f);
            _touchPitch = Mathf.Clamp(d.y / radius, -1f, 1f); // GUI y grows downwards: drag down = pull
        }

        private void HandleKeyboard(float dt, out float pitch, out float roll, out float yaw)
        {
            pitch = Key(KeyCode.S, KeyCode.DownArrow) - Key(KeyCode.W, KeyCode.UpArrow);
            roll = Key(KeyCode.D, KeyCode.RightArrow) - Key(KeyCode.A, KeyCode.LeftArrow);
            yaw = Key(KeyCode.E, KeyCode.None) - Key(KeyCode.Q, KeyCode.None);

            float thr = Key(KeyCode.LeftShift, KeyCode.Equals) - Key(KeyCode.LeftControl, KeyCode.Minus);
            if (thr != 0f) _throttle = Mathf.Clamp01(_throttle + thr * 0.5f * dt);

            if (Input.GetKey(KeyCode.RightBracket)) AdjustTrim(trimRatePerSec * dt);
            if (Input.GetKey(KeyCode.LeftBracket)) AdjustTrim(-trimRatePerSec * dt);

            if (Input.GetKeyDown(KeyCode.F)) _ac.CycleFlaps();
            if (Input.GetKeyDown(KeyCode.V)) ViewToggleRequested?.Invoke();
            if (Input.GetKeyDown(KeyCode.M)) ToggleMode();
            if (Input.GetKeyDown(KeyCode.R)) ResetRunwayRequested?.Invoke();
            if (Input.GetKeyDown(KeyCode.T)) ResetApproachRequested?.Invoke();
        }

        private static float Key(KeyCode a, KeyCode b) =>
            (Input.GetKey(a) || (b != KeyCode.None && Input.GetKey(b))) ? 1f : 0f;

        // ------------------------------------------------------------------ tilt

        private void UpdateTilt(float dt)
        {
            Vector3 raw = Input.acceleration;
            if (!_accValid) { _acc = raw; _accValid = raw.sqrMagnitude > 0.01f; }
            else _acc = Vector3.Lerp(_acc, raw, 1f - Mathf.Exp(-dt * 10f));

            ReadTiltAngles(out float rollDeg, out float pitchDeg);
            _tiltRoll = TiltAxis(rollDeg - _tiltRollZero, tiltRollRangeDeg);
            // Tilting the top edge towards you lowers pitchDeg -> pull (nose up).
            _tiltPitch = TiltAxis(_tiltPitchZero - pitchDeg, tiltPitchRangeDeg);
        }

        private float TiltAxis(float deg, float range)
        {
            float mag = Mathf.Max(0f, Mathf.Abs(deg) - tiltDeadzoneDeg);
            return Mathf.Clamp(Mathf.Sign(deg) * mag / range, -1f, 1f);
        }

        /// <summary>
        /// Input.acceleration is the gravity direction in device axes (portrait: +x right,
        /// +y up, +z out of the screen). Convert to the current landscape screen axes.
        /// rollDeg  > 0 when the right side of the screen is lowered (steering right).
        /// pitchDeg = 0 when the phone is upright, 90 when lying flat face up.
        /// </summary>
        private void ReadTiltAngles(out float rollDeg, out float pitchDeg)
        {
            Vector3 a = _acc;
            float sx, sy;
            if (Screen.orientation == ScreenOrientation.LandscapeRight) { sx = a.y; sy = -a.x; }
            else { sx = -a.y; sy = a.x; } // LandscapeLeft (home indicator on the right)
            float sz = a.z;

            rollDeg = Mathf.Atan2(sx, Mathf.Sqrt(sy * sy + sz * sz)) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Atan2(-sz, -sy) * Mathf.Rad2Deg;
        }

        private void CalibrateTilt()
        {
            if (!_accValid) { _acc = Input.acceleration; _accValid = _acc.sqrMagnitude > 0.01f; }
            ReadTiltAngles(out _tiltRollZero, out _tiltPitchZero);
        }

        // ------------------------------------------------------------------ layout (GUI coords)

        private Rect ThrottleRect()
        {
            Rect sa = GuiDraw.Safe;
            float top = sa.y + GuiDraw.U(150);
            return new Rect(sa.x + GuiDraw.U(16), top, GuiDraw.U(84), sa.yMax - GuiDraw.U(40) - top);
        }

        private bool ButtonVisible(Btn b) => b != Btn.Calibrate || Mode == ControlMode.Tilt;

        private Rect ButtonRect(Btn b)
        {
            Rect sa = GuiDraw.Safe;
            float u = GuiDraw.S;
            float colX = sa.x + 112f * u;
            switch (b)
            {
                case Btn.TrimUp: return new Rect(colX, sa.y + 150f * u, 110f * u, 64f * u);
                case Btn.TrimDown: return new Rect(colX, sa.y + 222f * u, 110f * u, 64f * u);
                case Btn.Flaps: return new Rect(colX, sa.yMax - 198f * u, 120f * u, 64f * u);
                case Btn.Brake: return new Rect(colX, sa.yMax - 124f * u, 120f * u, 90f * u);
            }

            // Top-right row, right to left: RESET, APPROACH, VIEW, CTRL, CENTER.
            int slot;
            switch (b)
            {
                case Btn.Reset: slot = 0; break;
                case Btn.Approach: slot = 1; break;
                case Btn.View: slot = 2; break;
                case Btn.Mode: slot = 3; break;
                default: slot = 4; break; // Calibrate
            }
            float w = 116f * u, h = 56f * u, gap = 10f * u;
            float x = sa.xMax - 12f * u - (slot + 1) * w - slot * gap;
            return new Rect(x, sa.y + 12f * u, w, h);
        }

        private string ButtonLabel(Btn b)
        {
            switch (b)
            {
                case Btn.Reset: return "RESET";
                case Btn.Approach: return "APPROACH";
                case Btn.View: return "VIEW";
                case Btn.Mode: return Mode == ControlMode.Touch ? "CTRL: TOUCH" : "CTRL: TILT";
                case Btn.Calibrate: return "CENTER";
                case Btn.TrimUp: return "TRIM UP";
                case Btn.TrimDown: return "TRIM DN";
                case Btn.Flaps: return $"FLAPS {_ac.FlapDeg}°";
                case Btn.Brake: return "BRAKE";
                default: return b.ToString();
            }
        }

        private bool ButtonActive(Btn b)
        {
            switch (b)
            {
                case Btn.Brake: return _brakeFinger != NoFinger || Input.GetKey(KeyCode.B);
                case Btn.TrimUp: return _trimFinger != NoFinger && _trimDir > 0;
                case Btn.TrimDown: return _trimFinger != NoFinger && _trimDir < 0;
                case Btn.Flaps: return _ac.FlapStage > 0;
                default: return false;
            }
        }

        /// <summary>Right part of the screen, below the top button row.</summary>
        private Rect StickZone()
        {
            Rect sa = GuiDraw.Safe;
            float left = sa.x + sa.width * 0.55f;
            float top = sa.y + GuiDraw.U(80);
            return new Rect(left, top, sa.xMax - left, sa.yMax - top);
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (_ac == null || Event.current.type != EventType.Repaint) return;
            GuiDraw.UpdateLayout();
            float u = GuiDraw.S;

            // Throttle slider
            Rect thr = ThrottleRect();
            GuiDraw.Fill(thr, new Color(0f, 0f, 0f, 0.35f));
            float fillH = thr.height * _throttle;
            GuiDraw.Fill(new Rect(thr.x, thr.yMax - fillH, thr.width, fillH), new Color(0.3f, 0.85f, 0.4f, 0.55f));
            GuiDraw.Frame(thr, Mathf.Max(1f, 2f * u), new Color(1f, 1f, 1f, 0.6f));
            float handleY = thr.yMax - fillH;
            GuiDraw.Fill(new Rect(thr.x - 6f * u, handleY - 4f * u, thr.width + 12f * u, 8f * u), Color.white);
            GuiDraw.Text(new Rect(thr.x, thr.y - 46f * u, thr.width, 40f * u), $"THR\n{_throttle * 100f:0}%", 16f, Color.white);

            foreach (var b in Buttons)
                if (ButtonVisible(b)) GuiDraw.Button(ButtonRect(b), ButtonLabel(b), ButtonActive(b), 18f);

            if (Mode == ControlMode.Touch)
            {
                if (_stickFinger != NoFinger)
                {
                    float r = GuiDraw.U(stickRadiusUnits);
                    Rect ring = new Rect(_stickOrigin.x - r, _stickOrigin.y - r, 2f * r, 2f * r);
                    GuiDraw.Fill(ring, new Color(0f, 0f, 0f, 0.2f));
                    GuiDraw.Frame(ring, Mathf.Max(1f, 2f * u), new Color(1f, 1f, 1f, 0.5f));
                    Vector2 knob = _stickOrigin + new Vector2(_touchRoll, _touchPitch) * r;
                    float k = 28f * u;
                    GuiDraw.Fill(new Rect(knob.x - k, knob.y - k, 2f * k, 2f * k), new Color(1f, 1f, 1f, 0.7f));
                }
                else
                {
                    Rect z = StickZone();
                    GuiDraw.Text(new Rect(z.x, z.yMax - 70f * u, z.width, 40f * u),
                        "drag here to fly  (down = nose up)", 16f, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter, false);
                }
            }
            else
            {
                Rect z = StickZone();
                GuiDraw.Text(new Rect(z.x, z.yMax - 70f * u, z.width, 40f * u),
                    $"TILT  roll {_tiltRoll:+0.00;-0.00}  pitch {_tiltPitch:+0.00;-0.00}", 16f,
                    new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter, false);
            }
        }
    }
}
