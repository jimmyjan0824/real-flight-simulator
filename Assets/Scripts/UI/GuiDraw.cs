using UnityEngine;

namespace FlightSim
{
    /// <summary>
    /// Tiny immediate-mode drawing helpers (IMGUI). The layout is designed on a 1280x720
    /// canvas and scaled by <see cref="S"/>; everything is placed inside the screen's safe
    /// area so the notch / Dynamic Island and home indicator never cover controls.
    /// GUI coordinates have their origin at the top-left (touch coordinates are bottom-left).
    /// </summary>
    public static class GuiDraw
    {
        public static float S { get; private set; } = 1f;
        /// <summary>Safe area in GUI coordinates (top-left origin), pixels.</summary>
        public static Rect Safe { get; private set; }

        private static GUIStyle _style;

        public static void UpdateLayout()
        {
            S = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Rect sa = Screen.safeArea;
            Safe = new Rect(sa.x, Screen.height - sa.yMax, sa.width, sa.height);
        }

        public static float U(float designUnits) => designUnits * S;

        public static Vector2 ScreenToGui(Vector2 screenPos) => new Vector2(screenPos.x, Screen.height - screenPos.y);

        public static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Frame(Rect r, float w, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        public static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            Fill(new Rect(a.x, a.y - width * 0.5f, len, width), c);
            GUI.matrix = saved;
        }

        public static void Text(Rect r, string text, float sizeUnits, Color c,
            TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = true)
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    richText = false,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };
            }
            _style.fontSize = Mathf.Max(8, Mathf.RoundToInt(sizeUnits * S));
            _style.alignment = anchor;
            _style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;

            float o = Mathf.Max(1f, 1.5f * S);
            _style.normal.textColor = new Color(0f, 0f, 0f, 0.75f * c.a);
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, _style);
            _style.normal.textColor = c;
            GUI.Label(r, text, _style);
        }

        public static void Button(Rect r, string label, bool active, float sizeUnits = 20f)
        {
            Fill(r, active ? new Color(1f, 0.75f, 0.2f, 0.55f) : new Color(0f, 0f, 0f, 0.35f));
            Frame(r, Mathf.Max(1f, 2f * S), new Color(1f, 1f, 1f, 0.6f));
            Text(r, label, sizeUnits, Color.white);
        }
    }
}
