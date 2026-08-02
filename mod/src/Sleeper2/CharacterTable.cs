using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The character window table (Phase 5; decode D18, owner rulings
    /// 2026-08-01 — CS1 CharacterTable is the template, CS2 deltas applied).
    /// Sections: the five skill rows (SKILL List/Individual Skills) over the
    /// class push tree (six nodes, three tiers, pairs mutually exclusive).
    ///
    /// CS2 deltas from CS1: no perks (ladder −1/0/+1/+2, DS &lt;SKILL&gt; 0–3, flat
    /// 4-point rungs), no arm step (rung buttons always live; only the NEXT
    /// rung's is interactable), one shared Upgrade Confirm Window (forced
    /// focus, CONFIRM preselected — the table stands down while it renders),
    /// and the HUB lock: outside hub scenes the game locks the WHOLE window
    /// read-only and renders LOCKED MESSAGE — spoken window-level on entry,
    /// restated on Enter (owner ruling 2). Blocked skills (&lt;SKILL&gt;_BLOCKED)
    /// render animator art only — the ruled word "blocked" is spoken from the
    /// state dial (ruling 3, the render-first sprite-only escape). Push: buy =
    /// native confirm flow; owned-node Enter = the game's free instant branch
    /// switch, mirrored natively with the config sentence spoken after
    /// (ruling 4). No programmatic open/close — closing checkpoints a save
    /// (ruling 5); the table binds only when the player opens the window.</summary>
    internal static class CharacterTable
    {
        private const float CacheWindow = 0.4f;

        private sealed class Row
        {
            public Transform Node;   // skill row root, or the push node button root
            public bool Push;
            public int Index;        // push node number 1..6
        }

        private static List<Row> _rows = new List<Row>();
        private static float _builtAt = -1f;
        private static bool _entered;
        private static bool _lockedSpoken;

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Rows().Count,
            Cols = row =>
            {
                var rows = Rows();
                return row >= 0 && row < rows.Count && !rows[row].Push ? 2 : 1;
            },
            RowSpeech = (r, c) => RowRead(r, c),
            CellSpeech = (r, c) => CellRead(r, c),
            SectionOf = row =>
            {
                var rows = Rows();
                return row >= 0 && row < rows.Count && rows[row].Push ? 1 : 0;
            },
            SectionPrefix = s => Lex.T(s == 1 ? "char.section.push" : "char.section.skills"),
            EmptyRow = () => Lex.T("char.empty"),
            EmptyCol = () => Lex.T("char.empty"),
            EmptyDetail = () => Lex.T("char.empty"),
            EmptyCommit = () => Lex.T("char.empty"),
            Source = "char",
        };

        public static void Init()
        {
            Table.Detail = (row, col) => Table.Say(FullReport(row));
            Table.Commit = (row, col) => Commit(row);
        }

        /// <summary>Keys in Mode.Character — except while the shared confirm
        /// window renders (native forced-focus dialog; focus reads carry it).</summary>
        public static bool Active()
        {
            if (ModeModel.Current() != Mode.Character) return false;
            if (GameQueries.CharacterConfirmUp()) return false;
            return true;
        }

        public static bool HandleKeys()
        {
            if (!_entered)
            {
                _entered = true;
                Table.Reset();
                // The HUB lock speaks window-level on entry (owner ruling 2).
                string locked = LockedText();
                if (locked != null && !_lockedSpoken)
                {
                    _lockedSpoken = true;
                    SpeechService.Say(locked, Priority.Queued, "char");
                }
            }
            return Table.HandleKeys();
        }

        /// <summary>Exit ONLY when the window itself closes (the game dial) —
        /// overlays above Character (pause, the window's own tutorial popup)
        /// are excursions: cursor, lock announcement, and any in-flight
        /// purchase watch all survive (sync pass F2/F6).</summary>
        public static void Tick()
        {
            if (!GameQueries.CharacterOpen())
            {
                if (_entered || _watchRow != null || _checkAt >= 0f)
                {
                    _entered = false;
                    _lockedSpoken = false;
                    _watchRow = null;
                    _checkAt = -1f;
                    Table.Reset();
                }
                return;
            }
            FeedbackTick();
        }

        // ---------- Rows ----------

        private static Transform Screen()
        {
            var go = GameObject.Find("Letterbox Canvas/Character Screen");
            return go != null ? go.transform : null;
        }

        private static List<Row> Rows()
        {
            if (Time.unscaledTime - _builtAt <= CacheWindow) return _rows;
            _builtAt = Time.unscaledTime;
            _rows = new List<Row>();
            var screen = Screen();
            if (screen == null) return _rows;
            var skills = screen.Find("SKILL List/Individual Skills");
            if (skills != null)
                foreach (Transform skill in skills)
                    if (skill.gameObject.activeInHierarchy)
                        _rows.Add(new Row { Node = skill });
            // Exactly one class push section is active (Portrait dispatch, D18).
            var list = screen.Find("SKILL List");
            if (list != null)
            {
                foreach (Transform child in list)
                {
                    if (!child.name.EndsWith(" Push Upgrades")) continue;
                    if (!child.gameObject.activeInHierarchy) continue;
                    var buttons = child.Find("Buttons");
                    if (buttons == null) continue;
                    foreach (Transform node in buttons)
                    {
                        if (!node.gameObject.activeInHierarchy) continue;
                        _rows.Add(new Row
                        {
                            Node = node,
                            Push = true,
                            Index = Util.TrailingInt(node.name),
                        });
                    }
                }
            }
            return _rows;
        }

        // ---------- Skill cells (ladder = the row FSM resting state, D18 (c)) ----------

        private static string RestingState(Transform skill)
        {
            var stack = skill.Find("Skill Upgrade Stack");
            var fsm = stack != null ? stack.GetComponent<PlayMakerFSM>() : null;
            return fsm != null ? fsm.ActiveStateName : null;
        }

        private static string ModifierWord(string state)
        {
            switch (state)
            {
                case "No Skill": return Lex.T("glyph.minus") + " 1";
                case "0 Skill": return "0";
                case "+ Skill": return Lex.T("glyph.plus") + " 1";
                case "++ Skill": return Lex.T("glyph.plus") + " 2";
                case "BLOCKED": return Lex.T("char.blocked");
                default: return null; // transient — no stable fact
            }
        }

        private static string SkillName(Transform skill)
        {
            var name = skill.Find("Skill Display/Name (1)");
            var tmp = name != null ? name.GetComponentInChildren<TMP_Text>(true) : null;
            string text = tmp != null ? SpeechService.Clean(tmp.text) : null;
            return !string.IsNullOrEmpty(text) ? text : skill.name;
        }

        private static string NameCell(Transform skill)
        {
            string modifier = ModifierWord(RestingState(skill));
            return SkillName(skill) + (modifier != null ? ", " + modifier : "") + ".";
        }

        private static string NextCell(Transform skill)
        {
            switch (RestingState(skill))
            {
                case "No Skill": return RungLine("0");
                case "0 Skill": return RungLine(Lex.T("glyph.plus") + " 1");
                case "+ Skill": return RungLine(Lex.T("glyph.plus") + " 2");
                case "++ Skill": return Lex.T("char.complete");
                case "BLOCKED": return Lex.T("char.blocked") + ".";
                default: return null;
            }
        }

        private static string RungLine(string rank)
            => Lex.T("char.rank") + " " + rank + ", "
             + Lex.T("char.costs") + " 4 " + Lex.T("char.points") + ".";

        // ---------- Push cells (D18 (e); owner ruling 1) ----------

        private static string PushName(Transform node)
        {
            var desc = node.Find("Description");
            var tmp = desc != null ? desc.GetComponentInChildren<TMP_Text>(true) : null;
            string text = tmp != null ? SpeechService.Clean(tmp.text) : null;
            return !string.IsNullOrEmpty(text) ? text : node.name;
        }

        /// <summary>The class push key ('REBOOT'/'RALLY'/'FOCUS') from the active
        /// section's own PUSH FSM variable — the documented D15 render pairing.</summary>
        private static string PushKey(Transform node)
        {
            var section = node.parent != null ? node.parent.parent : null;
            if (section == null) return null;
            foreach (var fsm in section.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                var v = fsm.FsmVariables.GetFsmString("PUSH");
                if (v != null && !string.IsNullOrEmpty(v.Value)) return v.Value;
            }
            return null;
        }

        private static Button NodeButton(Transform node)
        {
            var inner = node.Find("Push Upgrade 1");
            return inner != null ? inner.GetComponent<Button>()
                : node.GetComponentInChildren<Button>(true);
        }

        private static string PushRead(Row row)
        {
            var sb = new System.Text.StringBuilder(PushName(row.Node));
            int tier = (row.Index + 1) / 2;
            sb.Append(". ").Append(Lex.T("char.tier")).Append(' ').Append(tier).Append('.');
            string key = PushKey(row.Node);
            var owned = key != null ? LuaStore.Num(key + "_" + row.Index) : null;
            if (owned.HasValue && owned.Value >= 1)
            {
                sb.Append(' ').Append(Lex.T("char.owned"));
                var active = LuaStore.Num(key + "_" + row.Index + "_ACTIVE");
                if (active.HasValue && active.Value >= 1)
                    sb.Append(' ').Append(Lex.T("char.active"));
            }
            else
            {
                var button = NodeButton(row.Node);
                if (button != null && !button.IsInteractable())
                    sb.Append(' ').Append(Lex.T("zone.disabled"));
                else
                    sb.Append(' ').Append(Lex.T("char.costs")).Append(" 2 ")
                      .Append(Lex.T("char.points")).Append('.');
            }
            return sb.ToString();
        }

        // ---------- Speech ----------

        private static string RowRead(int row, int col)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("char.empty");
            var r = rows[row];
            if (r.Push) return PushRead(r);
            if (col <= 0)
                return NameCell(r.Node) + " " + Lex.T("char.col.next") + ": "
                     + (NextCell(r.Node) ?? Lex.T("char.empty"));
            // Held facet: name + cell (sync pass F7 — the map grammar).
            return NameCell(r.Node) + " " + CellRead(row, col);
        }

        private static string CellRead(int row, int col)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("char.empty");
            var r = rows[row];
            if (r.Push) return PushRead(r);
            return col <= 0 ? NameCell(r.Node)
                : Lex.T("char.col.next") + ": " + (NextCell(r.Node) ?? Lex.T("char.empty"));
        }

        private static string FullReport(int row)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("char.empty");
            var r = rows[row];
            if (r.Push)
            {
                // Full push detail: node + the game's own config sentence.
                string config = ConfigSentence();
                return PushRead(r) + (config != null ? " " + config : "");
            }
            return NameCell(r.Node) + " " + Lex.T("char.col.next") + ": "
                 + (NextCell(r.Node) ?? Lex.T("char.empty"))
                 + " " + PointsLine();
        }

        /// <summary>The game's own push-config summary sentence — EXACTLY the
        /// "Push Description" TMP under Push Config (sync pass F4: the sibling
        /// "Config" heading label comes first in hierarchy order and must not
        /// masquerade as the sentence).</summary>
        private static string ConfigSentence()
        {
            var screen = Screen();
            if (screen == null) return null;
            foreach (var tmp in screen.GetComponentsInChildren<TMP_Text>(false))
            {
                if (tmp.transform.name != "Push Description") continue;
                if (tmp.transform.parent == null
                    || tmp.transform.parent.name != "Push Config") continue;
                if (!Util.RenderedUp(tmp.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static string PointsLine()
        {
            var screen = Screen();
            var points = screen != null
                ? screen.Find("Upgrade Tracker/Points Av") : null;
            if (points == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var tmp in points.GetComponentsInChildren<TMP_Text>(false))
            {
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text)) sb.Append(text).Append(' ');
            }
            return sb.ToString().TrimEnd();
        }

        private static string LockedText()
        {
            var screen = Screen();
            var locked = screen != null ? screen.Find("LOCKED MESSAGE") : null;
            if (locked == null || !locked.gameObject.activeInHierarchy
                || !Util.RenderedUp(locked)) return null;
            var sb = new System.Text.StringBuilder();
            foreach (var tmp in locked.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text)) sb.Append(text).Append(". ");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
        }

        // ---------- Commit ----------

        private static void Commit(int row)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return;
            var r = rows[row];

            // The HUB lock: Enter restates the game's rendered reason (ruling 2).
            string locked = LockedText();
            if (locked != null) { Table.Say(locked); return; }

            Button button;
            if (r.Push) button = NodeButton(r.Node);
            else
            {
                // Only the NEXT rung's button is interactable (D18 (c)).
                button = null;
                var stack = r.Node.Find("Skill Upgrade Stack");
                if (stack != null)
                    foreach (var b in stack.GetComponentsInChildren<Button>(false))
                        if (b.IsInteractable()) { button = b; break; }
            }
            if (button == null || !button.IsInteractable())
            { Table.Say(Lex.T("char.unavailable")); return; }

            // Owned push node = the game's FREE instant branch switch (no
            // points gate, no confirm — sync pass F1): straight to click +
            // feedback; the affordability composition applies to BUYS only.
            bool ownedSwitch = false;
            if (r.Push)
            {
                string key = PushKey(r.Node);
                var owned = key != null ? LuaStore.Num(key + "_" + r.Index) : null;
                ownedSwitch = owned.HasValue && owned.Value >= 1;
            }

            // Not-enough-points restatement (the blocked-fuel composition family):
            // the game's refusal is a reactive flash + SFX only.
            int cost = r.Push ? 2 : 4;
            float points = ParsePoints();
            if (!ownedSwitch && points >= 0f && points < cost)
            {
                Navigator.Click(button.gameObject); // native flash + refusal SFX
                Table.Say(Lex.T("char.not-enough") + " " + Lex.T("map.requires")
                    + " " + cost + " " + Lex.T("char.points") + ".");
                return;
            }

            _watchRow = r;
            _pointsBefore = PointsLine();
            _confirmSpoken = false;
            _checkAt = Time.unscaledTime + 0.5f;
            Navigator.Click(button.gameObject);
        }

        private static float ParsePoints()
        {
            string line = PointsLine();
            if (string.IsNullOrEmpty(line)) return -1f;
            int i = 0;
            while (i < line.Length && !char.IsDigit(line[i])) i++;
            int start = i;
            while (i < line.Length && char.IsDigit(line[i])) i++;
            return i > start && float.TryParse(line.Substring(start, i - start), out float v)
                ? v : -1f;
        }

        // ---------- Purchase / switch feedback (CS1 compose, CS2 shapes) ----------

        private static Row _watchRow;
        private static string _pointsBefore;
        private static float _checkAt = -1f;
        private static bool _confirmSpoken;

        private static void FeedbackTick()
        {
            if (_checkAt < 0 || Time.unscaledTime < _checkAt) return;
            if (GameQueries.CharacterConfirmUp())
            {
                // Confirm window up: announce it once per commit (sync pass F5 —
                // the focus reannounce cooldown eats the forced CONFIRM focus on
                // back-to-back purchases), then re-clock until it resolves.
                if (!_confirmSpoken)
                {
                    _confirmSpoken = true;
                    SpeechService.Say(Lex.T("char.confirm"), Priority.Queued, "char");
                }
                _checkAt = Time.unscaledTime + 0.5f;
                return;
            }
            _checkAt = -1f;
            var r = _watchRow;
            _watchRow = null;
            if (r == null || r.Node == null || !GameQueries.CharacterOpen()) return;

            string after = PointsLine();
            bool spent = !string.IsNullOrEmpty(after) && after != _pointsBefore;
            var sb = new System.Text.StringBuilder();
            if (spent) sb.Append(after).Append(". ");
            if (r.Push)
            {
                // Owned-switch or purchase: the game's own config sentence is
                // the result of record (owner ruling 4).
                string config = ConfigSentence();
                sb.Append(config ?? PushRead(r));
            }
            else if (spent) sb.Append(NameCell(r.Node));
            else { sb.Append(Lex.T("char.no-change")); }
            Table.Say(sb.ToString());
        }
    }
}
