using System.Collections.Generic;

namespace HollowKnightPatches.Lower
{
    // Unity-free decisions for the HK lower shell (HKDualScreen.Shell.cs applies
    // them). Host tests compile this file directly.
    public static class HkShellPolicy
    {
        /// <summary>The shell shows unless the companion is off, a popup owns the panel,
        /// or gameplay faded the HUD (cutscene, lore tablet). Pause keeps it shown: the
        /// shell itself swaps to the title card.</summary>
        public static bool Visible(bool companionOn, bool popupAny, bool hudFaded, bool paused)
        {
            return companionOn && !popupAny && !(hudFaded && !paused);
        }

        /// <summary>Idle (pages hidden, logo shown) once out of gameplay for the grace period.
        /// Before the first gameplay frame there is nothing to wait for.</summary>
        public static bool Idle(bool inGame, bool everInGame, float idleSince, float now, float grace)
        {
            if (inGame) return false;
            return !everInGame || now - idleSince >= grace;
        }

        /// <summary>The live HUD's header band, converted from layout units to bottom-panel
        /// pixels (top-left origin). False when the layout or header has no size.</summary>
        public static bool HudBand(float layoutW, float layoutH, float headerW, float headerH,
                                   float panelW, float panelH, float pad, float bandWidth, float bandHeight,
                                   out float x, out float y, out float w, out float h)
        {
            x = y = w = h = 0f;
            if (layoutW <= 0f || layoutH <= 0f || headerW <= 0f || headerH <= 0f) return false;
            float sx = panelW / layoutW, sy = panelH / layoutH;
            float bandW = System.Math.Min(headerW * bandWidth, headerW - 2f * pad);
            float bandH = headerH * bandHeight - pad;
            x = pad * sx; y = pad * sy;
            w = System.Math.Max(1f, bandW * sx); h = System.Math.Max(1f, bandH * sy);
            return true;
        }
    }

    /// <summary>
    /// Turns the per-frame contact id set from the direct display into touch phases:
    /// a new id begins, a held id moves, a missing id ends at its last position.
    /// Cancel drops every held contact and bumps the generation so stale gestures die.
    /// </summary>
    public sealed class HkShellTouchTracker
    {
        public enum Phase { Began, Moved, Ended }

        public struct Contact
        {
            public int Id;
            public float X, Y;   // top-left normalised, 0..1
            public Contact(int id, float x, float y) { Id = id; X = x; Y = y; }
        }

        public struct Sample
        {
            public int Id;
            public Phase Phase;
            public float X, Y;   // layout units, bottom-left origin
        }

        readonly List<int> _held = new List<int>(4);
        readonly List<float> _heldX = new List<float>(4), _heldY = new List<float>(4);
        readonly List<int> _seen = new List<int>(4);

        public long Generation { get; private set; }
        public int HeldCount { get { return _held.Count; } }

        /// <summary>Normalised top-left contact to layout units (y up from inputHeight).</summary>
        public static void ToLayout(float nx, float ny, float layoutW, float layoutH, float inputHeight,
                                    out float x, out float y)
        {
            x = nx * layoutW;
            y = inputHeight - ny * layoutH;
        }

        /// <summary>Appends this frame's samples. A stale frame (no fresh contact set)
        /// ends every held contact.</summary>
        public void Update(IReadOnlyList<Contact> contacts, bool fresh, float layoutW, float layoutH,
                           float inputHeight, List<Sample> output)
        {
            _seen.Clear();
            if (fresh)
            {
                for (int i = 0; i < contacts.Count; i++)
                {
                    Contact c = contacts[i];
                    float x, y;
                    ToLayout(c.X, c.Y, layoutW, layoutH, inputHeight, out x, out y);
                    int held = _held.IndexOf(c.Id);
                    output.Add(new Sample { Id = c.Id, Phase = held < 0 ? Phase.Began : Phase.Moved, X = x, Y = y });
                    _seen.Add(c.Id);
                    if (held < 0) { _held.Add(c.Id); _heldX.Add(x); _heldY.Add(y); }
                    else { _heldX[held] = x; _heldY[held] = y; }
                }
            }
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                if (_seen.Contains(_held[i])) continue;
                output.Add(new Sample { Id = _held[i], Phase = Phase.Ended, X = _heldX[i], Y = _heldY[i] });
                _held.RemoveAt(i); _heldX.RemoveAt(i); _heldY.RemoveAt(i);
            }
        }

        public void Cancel()
        {
            _held.Clear(); _heldX.Clear(); _heldY.Clear();
            Generation++;
        }
    }
}
