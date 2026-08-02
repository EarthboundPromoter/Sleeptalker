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

            // Title save slots (owner bug report 2026-08-01: focus read ONE stray
            // field label per slot — "CYCLE button", "CLASS button"): anything
            // focused under MAIN MENU/…/Slot Menu/<slot> speaks the WHOLE slot —
            // its name plus every rendered text under the slot root in reading
            // order, the full save metadata as drawn.
            string saveSlot = SaveSlotCard(go);
            if (saveSlot != null) return saveSlot;

            // Dialogue skill-check responses carry a governing skill ("//INTERFACE")
            // — decision-relevant, appended to the response read. (The odds the
            // FSM renders on hover are spoken by SkillChecks off the tier-state
            // signal, so they follow this read as their own utterance.)
            string response = ResponseWithSkill(go);
            if (response != null) return response;

            // Dice cursors (the picker's native focus stops): die value/state read
            // from the cursor's own Die reference (decode D11).
            string die = DiceFlow.CursorRead(go);
            if (die != null) return die;

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

        /// <summary>Full slot read for the title save/continue menu (census:
        /// MAIN MENU/Demo Menu/Slot Menu/Slot N — the "Demo Menu" name is a
        /// wart). Live anatomy (bridge capture 2026-08-01): flat name-paired
        /// children — "Cycle Label"/"Cycle", "Class Label"/"Class", an EMPTY
        /// child for unused slots — and hierarchy order interleaves the pairs
        /// wrongly for speech (owner report). Composition is owner-ruled:
        /// "CYCLE, number. CLASS, name." Unpaired rendered text still appends
        /// (never silently dropped). Null off the slot menu.</summary>
        private static readonly string[] SlotFields = { "Cycle", "Class" };

        private static string SaveSlotCard(GameObject go)
        {
            if (!Util.HasAncestor(go, "MAIN MENU")) return null;
            Transform slot = null;
            for (var cur = go.transform; cur != null; cur = cur.parent)
            {
                if (cur.parent != null && cur.parent.name == "Slot Menu")
                { slot = cur; break; }
            }
            if (slot == null) return null;

            var sb = new System.Text.StringBuilder(slot.name).Append('.');
            var consumed = new System.Collections.Generic.List<Transform>();

            var empty = slot.Find("EMPTY");
            if (empty != null && empty.gameObject.activeInHierarchy
                && Util.RenderedUp(empty))
            {
                // Unused slot: the EMPTY child is the whole story.
                AppendTexts(sb, empty);
                consumed.Add(empty);
            }
            else
            {
                foreach (var field in SlotFields)
                {
                    // Name-TOLERANT matching (ride V5: Slot 2/3 children carry
                    // clone-suffixed names — prefix match, Label discriminates).
                    Transform label = null, value = null;
                    foreach (Transform child in slot)
                    {
                        if (!child.name.StartsWith(field)) continue;
                        if (child.name.Contains("Label")) { if (label == null) label = child; }
                        else if (value == null) value = child;
                    }
                    string labelText = label != null && Util.RenderedUp(label)
                        ? SpeechService.Clean(GetTmp(label)) : null;
                    string valueText = value != null && Util.RenderedUp(value)
                        ? SpeechService.Clean(GetTmp(value)) : null;
                    if (label != null) consumed.Add(label);
                    if (value != null) consumed.Add(value);
                    if (string.IsNullOrEmpty(labelText) && string.IsNullOrEmpty(valueText))
                        continue;
                    sb.Append(' ')
                      .Append(!string.IsNullOrEmpty(labelText) ? labelText : field)
                      .Append(", ")
                      .Append(valueText ?? "").Append('.');
                }
            }

            // Anything rendered the pairing didn't cover still speaks.
            foreach (var tmp in slot.GetComponentsInChildren<TMP_Text>(false))
            {
                bool skip = false;
                foreach (var root in consumed)
                    if (tmp.transform == root || tmp.transform.IsChildOf(root))
                    { skip = true; break; }
                if (skip) continue;
                if (!Util.RenderedUp(tmp.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text)) continue;
                sb.Append(' ').Append(text);
                if (!text.EndsWith(".")) sb.Append('.');
            }
            return sb.ToString();
        }

        private static void AppendTexts(System.Text.StringBuilder sb, Transform root)
        {
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text)) continue;
                sb.Append(' ').Append(text);
                if (!text.EndsWith(".")) sb.Append('.');
            }
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

        // ---------- Rendered-text lookups (shared by the card/clock tables) ----------

        /// <summary>Cleaned text of the first RENDERED descendant named exactly
        /// <paramref name="childName"/>; null when absent, hidden, or textless.
        /// Rendered = active AND effective alpha up — object presence is not screen
        /// presence (founding CS2 rule; ride V1 caught the PREDICTIVE block active
        /// but alpha-hidden until the perk draws it).</summary>
        public static string TextUnder(Transform root, string childName)
        {
            if (root == null) return null;
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (tmp.gameObject.name != childName) continue;
                if (!Util.RenderedUp(tmp.transform, root.parent)) continue;
                string text = SpeechService.Clean(tmp.text);
                return string.IsNullOrEmpty(text) ? null : text;
            }
            return null;
        }

        /// <summary>Cleaned text of the first RENDERED descendant whose text contains
        /// <paramref name="fragment"/> (case-sensitive; alpha-honest, as above).</summary>
        public static string TextContaining(Transform root, string fragment)
        {
            if (root == null) return null;
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform, root.parent)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text) && text.Contains(fragment)) return text;
            }
            return null;
        }

        /// <summary>Strips one layer of surrounding straight or curly quotes
        /// (clock names render quoted in both games).</summary>
        public static string TrimQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.Length >= 2
                && (s[0] == '"' || s[0] == '“')
                && (s[s.Length - 1] == '"' || s[s.Length - 1] == '”'))
                return s.Substring(1, s.Length - 2).Trim();
            return s;
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
            if (!Util.RenderedUp(t, root.parent)) return true;
            for (var cur = t; cur != null && cur != root.parent; cur = cur.parent)
            {
                string n = cur.name;
                if (n.Contains("Prompt") || n.Contains("Glyph")) return true;
            }
            return false;
        }
    }
}
