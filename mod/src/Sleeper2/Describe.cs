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

            // Difficulty select (owner ruling, first CS2 ride): highlighting a
            // difficulty auto-reads its full metadata — name, selected state, and
            // the rendered description lines. Everything else on that screen
            // (Tutorials toggle, Confirm, Back) reads as a plain element.
            string difficulty = DifficultyCard(go);
            if (difficulty != null) return difficulty;

            // Dialogue skill-check responses carry a governing skill ("//INTERFACE")
            // — decision-relevant, appended to the response read. (The odds the
            // FSM renders on hover are spoken by SkillChecks off the tier-state
            // signal, so they follow this read as their own utterance.)
            string response = ResponseWithSkill(go);
            if (response != null) return response;

            // Station location nodes: the camera-proximity selector focuses the
            // marker's Location Button (live capture 2026-07-26); identity renders
            // in the sibling billboard. Census-universal vocabulary, all families.
            string location = LocationNode(go);
            if (location != null) return location;

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

        /// <summary>Full metadata read for a difficulty button (child of the
        /// Difficulty Menu's BUTTONS container; hierarchy captured live 2026-07-26).
        /// Null for anything else — the general path handles it.</summary>
        private static string DifficultyCard(GameObject go)
        {
            Transform card = null;
            for (var cur = go.transform; cur != null; cur = cur.parent)
            {
                if (cur.parent != null && cur.parent.name == "BUTTONS"
                    && Util.HasAncestor(cur.parent.gameObject, "Difficulty Menu"))
                {
                    card = cur;
                    break;
                }
            }
            if (card == null) return null;

            var sb = new System.Text.StringBuilder();
            var nameNode = card.Find("Name");
            var nameText = nameNode != null ? SpeechService.Clean(GetTmp(nameNode)) : null;
            sb.Append(nameText ?? card.name);

            foreach (Transform child in card)
            {
                if (child.name.Contains("SELECTED") && child.gameObject.activeInHierarchy)
                {
                    sb.Append(". Selected");
                    break;
                }
            }

            foreach (Transform child in card)
            {
                if (!child.name.StartsWith("Description")) continue;
                if (!child.gameObject.activeInHierarchy) continue;
                string body = GetTmp(child);
                if (string.IsNullOrEmpty(body)) continue;
                foreach (var raw in body.Split('\n'))
                {
                    // "- " bullets: the dash marks a list line; spoken form is the
                    // line as its own sentence.
                    string line = SpeechService.Clean(raw.TrimStart(' ', '-'));
                    if (!string.IsNullOrEmpty(line))
                    {
                        sb.Append(". ").Append(line.TrimEnd('.'));
                    }
                }
            }
            sb.Append('.');
            return sb.ToString();
        }

        private static string GetTmp(Transform t)
        {
            var tmp = t.GetComponent<TMP_Text>();
            return tmp != null ? tmp.text : null;
        }

        /// <summary>Response buttons: text + governing skill when the response is a
        /// skill check ("Response Text/Skill Name" renders "//INTERFACE").</summary>
        private static string ResponseWithSkill(GameObject go)
        {
            if (!go.name.StartsWith("Response: ")) return null;
            var responseText = go.transform.Find("Response Text");
            if (responseText == null) return null;
            string text = SpeechService.Clean(GetTmp(responseText));
            if (string.IsNullOrEmpty(text)) return null;
            var skillNode = responseText.Find("Skill Name");
            string skill = skillNode != null ? SpeechService.Clean(GetTmp(skillNode)) : null;
            if (!string.IsNullOrEmpty(skill))
                return text + " " + skill.TrimStart('/') + " check.";
            return null; // plain response: the general path reads it
        }

        /// <summary>Location node read: "PORTRAIT NAME. Description." from the
        /// billboard above the focused Location Button (Marker -> Billboard
        /// Elements -> Portrait Name -> Description). Null off-family so the
        /// general path handles anything else.</summary>
        private static string LocationNode(GameObject go)
        {
            if (go.name != "Location Button") return null;
            for (var cur = go.transform; cur != null; cur = cur.parent)
            {
                if (cur.name != "Billboard Elements") continue;
                var nameNode = cur.Find("Portrait Name");
                if (nameNode == null || !nameNode.gameObject.activeInHierarchy) return null;
                string title = SpeechService.Clean(GetTmp(nameNode));
                if (string.IsNullOrEmpty(title)) return null;
                var descNode = nameNode.Find("Description");
                string desc = descNode != null && descNode.gameObject.activeInHierarchy
                    ? SpeechService.Clean(GetTmp(descNode)) : null;
                return title + (!string.IsNullOrEmpty(desc) ? ". " + desc + "." : ".");
            }
            return null;
        }

        private static string GetTmpDeep(Transform t)
        {
            var tmp = t.GetComponentInChildren<TMP_Text>(false);
            return tmp != null ? tmp.text : null;
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
