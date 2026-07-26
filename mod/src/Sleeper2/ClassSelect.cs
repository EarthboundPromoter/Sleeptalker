using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Class select (owner design, first CS2 ride): a walkable 1-D review
    /// over the class card — Up/Down step whole rows (name, description, push
    /// lines, per-skill spread), one step past the end reaches the CONFIRM CLASS
    /// button, Up from there re-enters the table. Left/Right swap classes via the
    /// Moving Panel carousel FSM's own LEFT/RIGHT events; a swap announces only the
    /// landing class name (no auto-read — the ruling), edges repeat the name
    /// (dead end, no wrap). Enter inside the table jumps to the confirm row first
    /// (real focus never leaves the button, so a bare Enter would lock the class
    /// silently — PROVISIONAL guard, owner ruling pending). Rows rebuilt fresh per
    /// keypress from rendered text. PROVISIONAL WORDING: skill dials "+ Skill"/
    /// "0 Skill"/"No Skill"/"BLOCKED" spoken as plus one/zero/minus one/blocked.
    /// Wart: the MACHINIST panel object is spelled "MACHINST" — panels matched by
    /// rendered Class Name, never object name.</summary>
    internal static class ClassSelect
    {
        private const string Root = "MAIN MENU/Demo Menu/Character Select Canvas";
        private const float NameDefer = 0.35f;
        private const float DeadEndAfter = 0.45f;

        private static int _cursor;           // 0..rows-1 in the table; rows.Count = confirm row
        private static float _nameAt = -1f;
        private static float _deadEndCheckAt = -1f;
        private static string _classAtSwap;

        private static readonly string[] ClassStates = { "OPERATOR", "MACHINIST", "EXTRACTOR" };

        // The menu stack stays activeInHierarchy across the whole title scene
        // (live finding: the class canvas was "active" at the press-start gate) —
        // object presence is NOT screen presence here. The MAIN MENU FSM's state
        // IS the current screen; "Class Select" is this one.
        private static PlayMakerFSM _mainMenu;

        public static bool Active()
        {
            if (_mainMenu == null)
                _mainMenu = GameQueries.FindFsm("MAIN MENU");
            return _mainMenu != null && _mainMenu.ActiveStateName == "Class Select";
        }

        public static void Init()
        {
            FsmSignals.Subscribe("Moving Panel", null, (fsm, state) =>
            {
                if (!IsClassState(state)) return;
                if (fsm == null || !Util.HasAncestor(fsm.gameObject, "Character Select Canvas")) return;
                // Boot/off-screen entries (the carousel starts in a class state at
                // scene load) must not speak — only entries while the screen is up.
                if (!Active()) return;
                _cursor = 0;
                _nameAt = Time.unscaledTime + NameDefer;
                _deadEndCheckAt = -1f;
            });
        }

        private static bool IsClassState(string state)
        {
            foreach (var s in ClassStates)
                if (s == state) return true;
            return false;
        }

        // ---------- Left/Right: carousel swap ----------

        public static void Swap(int direction)
        {
            var fsm = GameQueries.FindFsm("Moving Panel", "Character Select");
            if (fsm == null) return;
            string state = fsm.ActiveStateName;
            if (!IsClassState(state)) return; // mid-transition: let it finish
            _classAtSwap = state;
            fsm.SendEvent(direction < 0 ? "LEFT" : "RIGHT");
            _deadEndCheckAt = Time.unscaledTime + DeadEndAfter;
        }

        // ---------- Up/Down: row walk + confirm handoff ----------

        public static void Browse(int delta)
        {
            var rows = BuildRows();
            if (rows.Count == 0)
            {
                SpeechService.Say("No class details.", Priority.Immediate, "class");
                return;
            }
            int confirmRow = rows.Count;
            _cursor = Mathf.Clamp(_cursor + delta, 0, confirmRow);
            if (_cursor == confirmRow)
            {
                var confirm = GameObject.Find(Root + "/Choose Class");
                if (confirm != null)
                {
                    Navigator.Select(confirm); // announces via the focus channel
                    return;
                }
                _cursor = confirmRow - 1; // no button found: clamp into the table
            }
            SpeechService.Say(rows[_cursor], Priority.Immediate, "class");
        }

        /// <summary>Enter: inside the table, jump to the confirm row (guard against
        /// silent class lock-in); on the confirm row, let the native activate run.
        /// Returns true when the key was consumed.</summary>
        public static bool EnterKey()
        {
            var rows = BuildRows();
            if (_cursor >= rows.Count) return false; // on confirm: native path
            _cursor = rows.Count;
            var confirm = GameObject.Find(Root + "/Choose Class");
            if (confirm != null) { Navigator.Select(confirm); return true; }
            return false;
        }

        public static void Tick()
        {
            if (_nameAt >= 0 && Time.unscaledTime >= _nameAt)
            {
                _nameAt = -1f;
                string cls = CurrentClass();
                if (cls != null)
                    SpeechService.Say(cls + ".", Priority.Immediate, "class");
            }

            if (_deadEndCheckAt >= 0 && Time.unscaledTime >= _deadEndCheckAt)
            {
                _deadEndCheckAt = -1f;
                var fsm = GameQueries.FindFsm("Moving Panel", "Character Select");
                if (fsm != null && fsm.ActiveStateName == _classAtSwap)
                    SpeechService.Say(_classAtSwap + ".", Priority.Immediate, "class");
            }
        }

        // ---------- Row composition (rendered text, fresh per keypress) ----------

        private static string CurrentClass()
        {
            var fsm = GameQueries.FindFsm("Moving Panel", "Character Select");
            return fsm != null && IsClassState(fsm.ActiveStateName) ? fsm.ActiveStateName : null;
        }

        private static List<string> BuildRows()
        {
            var rows = new List<string>();
            var root = GameObject.Find(Root);
            string cls = CurrentClass();
            if (root == null || cls == null) return rows;

            var movingPanel = root.transform.Find("Moving Panel");
            if (movingPanel == null) return rows;

            Transform panel = null;
            foreach (Transform child in movingPanel)
            {
                var nameText = TextOf(child.Find("Class Name"));
                if (nameText != null && nameText.Equals(cls, System.StringComparison.OrdinalIgnoreCase))
                {
                    panel = child;
                    break;
                }
            }
            if (panel == null) return rows;

            // Row 0: class name + class description merged (owner row model).
            string desc = TextOf(panel.Find("DESC"));
            rows.Add((TextOf(panel.Find("Class Name")) ?? cls) + "." + (desc != null ? " " + desc : ""));

            // Row 1: the Push ability as one row — name, effect, stress config.
            foreach (Transform child in panel)
            {
                if (!child.name.EndsWith(" Push")) continue;
                var push = child.Find("PUSH");
                if (push == null || !push.gameObject.activeInHierarchy) continue;
                var sb = new StringBuilder("Push: ");
                string pushName = TextOf(push);
                if (pushName != null) sb.Append(pushName).Append(". ");
                string pushDesc = TextOf(push.Find("Skill Descs"));
                if (pushDesc != null) sb.Append(pushDesc).Append(' ');
                string config = TextOf(push.Find("Push Config/Push Description"));
                if (config != null) sb.Append(config.Replace("//", ", ")).Append(". ");
                string bonus = TextOf(push.Find("Push Config/Bonus 2/Bonus Desc"));
                if (bonus != null) sb.Append(bonus).Append('.');
                rows.Add(sb.ToString().TrimEnd());
                break;
            }

            var skills = root.transform.Find("Character Window/SKILL List/Individual Skills");
            if (skills != null)
            {
                foreach (Transform skill in skills)
                {
                    var stack = skill.Find("Skill Upgrade Stack");
                    var dial = stack != null ? stack.GetComponent<PlayMakerFSM>() : null;
                    if (dial == null) continue;
                    string word = DialWord(dial.ActiveStateName);
                    if (word != null) rows.Add(skill.name + " " + word + ".");
                }
            }
            return rows;
        }

        private static string DialWord(string state)
        {
            switch (state)
            {
                case "+ Skill": return "plus one";
                case "0 Skill": return "zero";
                case "No Skill": return "minus one";
                case "BLOCKED": return "blocked";
                default: return null;
            }
        }

        private static string TextOf(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return null;
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp == null) tmp = t.GetComponentInChildren<TMP_Text>(false);
            return tmp != null ? SpeechService.Clean(tmp.text) : null;
        }
    }
}
