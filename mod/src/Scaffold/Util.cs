using System.Text;
using UnityEngine;

namespace Sleeptalker.Scaffold
{
    /// <summary>Game-agnostic helpers (consolidation pass, audit 2026-07-22 A2/A5/A6:
    /// each of these existed as two to five private copies). No game knowledge lives
    /// here — game-specific lookups stay in the Game/UI tiers.</summary>
    internal static class Util
    {
        /// <summary>Effective CanvasGroup visibility of t: multiply alphas from t up
        /// to (not including) stopAt, or all the way to the root when stopAt is null.
        /// UI that hides by alpha (Animator-driven windows, notification templates,
        /// dismissed tutorial panels) needs this walk — activeInHierarchy alone reads
        /// invisible panels as shown.</summary>
        public static float AlphaUpTo(Transform t, Transform stopAt = null)
        {
            float alpha = 1f;
            for (var cur = t; cur != null && cur != stopAt; cur = cur.parent)
            {
                var g = cur.GetComponent<CanvasGroup>();
                if (g != null) alpha *= g.alpha;
            }
            return alpha;
        }

        /// <summary>True when go or any ancestor bears exactly this name.</summary>
        public static bool HasAncestor(GameObject go, string name)
        {
            for (var cur = go.transform; cur != null; cur = cur.parent)
                if (cur.name == name) return true;
            return false;
        }

        /// <summary>Full hierarchy path of go ("Letterbox Canvas/Top UI/...").</summary>
        public static string PathOf(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            for (var t = go.transform.parent; t != null; t = t.parent)
                sb.Insert(0, t.name + "/");
            return sb.ToString();
        }

        /// <summary>Leading integer of a name ("32 Step Clock" -> 32); 0 if none.</summary>
        public static int LeadingInt(string name)
        {
            int i = 0;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            int.TryParse(name.Substring(0, i), out int result);
            return result;
        }

        /// <summary>Trailing integer of a name ("Dice Slot 3" -> 3); 0 if none.</summary>
        public static int TrailingInt(string name)
        {
            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            int.TryParse(name.Substring(i + 1), out int result);
            return result;
        }

        /// <summary>Parse the leading glyph run of a rendered effect entry ("-- ENERGY",
        /// "+ 15 CRYO"): counts '+' and '-' through any interleaved spaces and returns
        /// the index where the run ends (the body start). Parsing only — what a run
        /// MEANS is per-surface policy (effect state, predicted tiers, the cycle
        /// strip), deliberately not unified (audit A9 ruling 2026-07-26).</summary>
        public static int GlyphRun(string s, out int plus, out int minus)
        {
            plus = 0; minus = 0;
            int i = 0;
            while (i < s.Length && (s[i] == '+' || s[i] == '-' || s[i] == ' '))
            {
                if (s[i] == '+') plus++;
                else if (s[i] == '-') minus++;
                i++;
            }
            return i;
        }
    }
}
