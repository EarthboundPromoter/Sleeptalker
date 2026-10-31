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
    /// (dead end, no wrap). Enter ALWAYS clicks CONFIRM CLASS and advances, from
    /// anywhere in the table (owner re-ruling 2026-07-26 — the earlier mid-table
    /// guard is superseded). Rows rebuilt fresh per
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
        /// <summary>The class whose landing was already spoken at the key press
        /// (2026-10-25), so the carousel's own arrival does not read it again.</summary>
        private static string _spokenAtKey;
        private static float _swapSpokenAt = -10f;

        /// <summary>A press made while the carousel slides (2026-10-27): the game ignores
        /// it, so the mod keeps the class it asked for, speaks it at the press, and steps
        /// there with the carousel's own event once it rests (Tick).</summary>
        private static string _queuedTarget;

        /// <summary>FocusPatch: the CONFIRM CLASS re-selection the carousel makes
        /// after a slide we already announced is not news.</summary>
        public static bool SuppressConfirmFocus(GameObject selected)
            => selected != null && selected.name == "Choose Class"
               && Time.unscaledTime - _swapSpokenAt < 1.5f;

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
                _deadEndCheckAt = -1f;
                // A queued press is still stepping: a class it passes is not news.
                if (_queuedTarget != null && state != _queuedTarget) return;
                if (state == _spokenAtKey) { _spokenAtKey = null; return; }
                _spokenAtKey = null;
                _nameAt = Time.unscaledTime + NameDefer;
            });
        }

        private static bool IsClassState(string state)
        {
            foreach (var s in ClassStates)
                if (s == state) return true;
            return false;
        }

        // ---------- Left/Right: carousel swap ----------

        /// <summary>Left/Right (2026-10-25): the destination is known from the
        /// carousel FSM's own transitions at the key press, so it is spoken then
        /// instead of after the slide. An edge (no transition for the event) says
        /// so at once; a press mid-slide says where the slide is heading.</summary>
        public static void Swap(int direction)
        {
            var fsm = GameQueries.FindFsm("Moving Panel", "Character Select");
            if (fsm == null) return;
            string state = fsm.ActiveStateName;
            string ev = direction < 0 ? "LEFT" : "RIGHT";
            if (!IsClassState(state) || _queuedTarget != null)
            {
                // Mid-transition the game ignores the press. Count it from where the
                // slide is heading (or the class already queued), speak that class
                // now, and step there once the carousel rests (Tick).
                string heading = Destination(fsm, state);
                if (heading == null) return;
                string from = _queuedTarget ?? heading;
                string next = Neighbour(fsm, from, ev, out _);
                if (next == null)
                {
                    SpeakLanding(from, direction < 0 ? "First class. " : "Last class. ");
                    return;
                }
                _queuedTarget = next == heading ? null : next;
                _cursor = 0;
                _nameAt = -1f;
                _spokenAtKey = next;
                SpeakLanding(next, "");
                return;
            }
            string target = Neighbour(fsm, state, ev, out bool known);
            if (known && target == null)
            {
                SpeakLanding(state, direction < 0 ? "First class. " : "Last class. ");
                return;
            }
            _classAtSwap = state;
            fsm.SendEvent(ev);
            if (target != null && Destination(fsm, fsm.ActiveStateName) == target)
            {
                _cursor = 0;
                _nameAt = -1f;
                SpeakLanding(target, "");
                _spokenAtKey = target;
                return;
            }
            // Unknown carousel shape: the old path (landing read, dead-end check).
            _deadEndCheckAt = Time.unscaledTime + DeadEndAfter;
        }

        /// <summary>The class a state is, or the class its FINISHED leads to (the
        /// carousel's tween states).</summary>
        private static string Destination(PlayMakerFSM fsm, string stateName)
        {
            if (stateName == null) return null;
            if (IsClassState(stateName)) return stateName;
            var st = fsm.Fsm.GetState(stateName);
            if (st == null) return null;
            foreach (var t in st.Transitions)
                if (t.EventName == "FINISHED" && IsClassState(t.ToState)) return t.ToState;
            return null;
        }

        /// <summary>Where LEFT/RIGHT leads from a class state. known=false when the
        /// state cannot be read (then the old dead-end check decides).</summary>
        private static string Neighbour(PlayMakerFSM fsm, string classState, string ev, out bool known)
        {
            known = false;
            var st = fsm.Fsm.GetState(classState);
            if (st == null) return null;
            known = true;
            foreach (var t in st.Transitions)
            {
                if (t.EventName != ev) continue;
                string d = Destination(fsm, t.ToState);
                if (d == null) known = false; // a shape this reader does not know
                return d;
            }
            return null;
        }

        /// <summary>A landing read: the class's top row (name + description, the
        /// owner's landing ruling) with its place in the carousel.</summary>
        private static void SpeakLanding(string cls, string prefix)
        {
            _swapSpokenAt = Time.unscaledTime;
            var panel = PanelFor(cls);
            string name = (panel != null ? TextOf(panel.Find("Class Name")) : null) ?? cls;
            string desc = panel != null ? TextOf(panel.Find("DESC")) : null;
            string pos = Position(cls);
            SpeechService.Say(prefix + name + (pos != null ? ", " + pos : "") + "."
                + (desc != null ? " " + desc : ""), Priority.Immediate, "class");
        }

        /// <summary>The carousel panel showing this class, matched by rendered Class
        /// Name (the MACHINST object-name wart).</summary>
        private static Transform PanelFor(string cls)
        {
            var root = GameObject.Find(Root);
            var movingPanel = root != null ? root.transform.Find("Moving Panel") : null;
            if (movingPanel == null) return null;
            foreach (Transform child in movingPanel)
            {
                var nameText = TextOf(child.Find("Class Name"));
                if (nameText != null && nameText.Equals(cls, System.StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }

        /// <summary>"class N of 3", counted from the left by the panels' own layout,
        /// so Left always counts down. Null when the layout cannot be read.</summary>
        private static string Position(string cls)
        {
            var self = PanelFor(cls);
            if (self == null || self.parent == null) return null;
            int rank = 1, total = 0;
            foreach (Transform child in self.parent)
            {
                string n = TextOf(child.Find("Class Name"));
                if (n == null || !IsClassState(n.ToUpperInvariant())) continue;
                total++;
                if (child != self && child.localPosition.x < self.localPosition.x) rank++;
            }
            return total > 1 ? "class " + rank + " of " + total : null;
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
                    _swapSpokenAt = -10f; // our own landing on it always speaks
                    Navigator.Select(confirm); // announces via the focus channel
                    return;
                }
                _cursor = confirmRow - 1; // no button found: clamp into the table
            }
            SpeechService.Say(rows[_cursor], Priority.Immediate, "class");
        }

        /// <summary>Enter (owner re-ruling 2026-07-26, superseding the mid-table
        /// guard): Enter ALWAYS clicks CONFIRM CLASS and advances — the table reads
        /// freely up and down but never stops Enter from confirming. Explicitly
        /// targets the confirm button rather than trusting focus, same discipline
        /// as the tutorial CONTINUE ruling. (Difficulty select deliberately keeps
        /// focused-element activation: the Tutorials toggle lives in that flow.)
        /// Returns true when the key was handled.</summary>
        public static bool EnterKey()
        {
            var confirm = GameObject.Find(Root + "/Choose Class");
            if (confirm == null) return false; // no button found: native path
            Navigator.Click(confirm);
            return true;
        }

        /// <summary>Does repeating this event from a class reach the target?</summary>
        private static bool Reaches(PlayMakerFSM fsm, string from, string ev, string target)
        {
            string cur = from;
            for (int i = 0; i < ClassStates.Length && cur != null; i++)
            {
                cur = Neighbour(fsm, cur, ev, out _);
                if (cur == target) return true;
            }
            return false;
        }

        public static void Tick()
        {
            if (_queuedTarget != null)
            {
                var fsm = Active() ? GameQueries.FindFsm("Moving Panel", "Character Select") : null;
                string st = fsm != null ? fsm.ActiveStateName : null;
                if (fsm == null || st == _queuedTarget) _queuedTarget = null;
                else if (IsClassState(st))
                {
                    // Rested on the way: the next step, silently (spoken at the press).
                    _swapSpokenAt = Time.unscaledTime;
                    fsm.SendEvent(Reaches(fsm, st, "RIGHT", _queuedTarget) ? "RIGHT" : "LEFT");
                    if (Destination(fsm, fsm.ActiveStateName) == st) _queuedTarget = null; // did not move
                }
            }

            if (_nameAt >= 0 && Time.unscaledTime >= _nameAt)
            {
                _nameAt = -1f;
                // Owner ruling: a landing (screen load or swap) reads the full top
                // row — name + description — with the cursor already on it.
                string cls = CurrentClass();
                if (cls != null) SpeakLanding(cls, "");
            }

            if (_deadEndCheckAt >= 0 && Time.unscaledTime >= _deadEndCheckAt)
            {
                _deadEndCheckAt = -1f;
                var fsm = GameQueries.FindFsm("Moving Panel", "Character Select");
                if (fsm != null && fsm.ActiveStateName == _classAtSwap)
                    SpeakLanding(_classAtSwap, "");
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
