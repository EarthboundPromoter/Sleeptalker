using TMPro;
using UnityEngine;
using UnityEngine.UI;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Spoken descriptions of CS2 UI elements. Skeleton stage: rendered-label
    /// harvest + role suffix — the CS1 conventions (skill rows, action cards, item
    /// cells) arrive with their surfaces, corpus-verified per surface.</summary>
    internal static class Describe
    {
        /// <summary>The ElementDescriber seam target: label + role for any focused
        /// element. Rendered text only; falls back to the object name so a missing
        /// label is audible, never silent.</summary>
        public static string Element(GameObject go, bool detailed)
        {
            if (go == null) return null;
            string label = FirstText(go);
            if (!string.IsNullOrEmpty(label))
            {
                var selectable = go.GetComponent<Selectable>();
                string role = selectable is Button ? " button" : "";
                if (selectable is Toggle toggle)
                    role = toggle.isOn ? " toggle, on" : " toggle, off";
                return label + role;
            }
            return go.name;
        }

        /// <summary>First non-empty rendered text on go or its descendants, skipping
        /// controller prompt glyphs (their texts are button-icon codes, not labels)
        /// and alpha-hidden subtrees.</summary>
        public static string FirstText(GameObject go)
        {
            foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(false))
            {
                if (SkipTextNode(tmp.transform, go.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            foreach (var legacy in go.GetComponentsInChildren<Text>(false))
            {
                if (SkipTextNode(legacy.transform, go.transform)) continue;
                string text = SpeechService.Clean(legacy.text);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static bool SkipTextNode(Transform t, Transform root)
        {
            if (Util.AlphaUpTo(t, root.parent) < 0.05f) return true;
            for (var cur = t; cur != null && cur != root.parent; cur = cur.parent)
            {
                string n = cur.name;
                if (n.Contains("Prompt") || n.Contains("Glyph")) return true;
            }
            return false;
        }
    }
}
