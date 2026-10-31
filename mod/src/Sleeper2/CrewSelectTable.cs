using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The map's crew selection window as a table (owner request
    /// 2026-10-30; supersedes the 2026-08-02 "native arrows keep the walking"
    /// ruling). The game's own navigation is spatial: Up from TRAVEL TO
    /// CONTRACT reached Juni, Right skipped every crew member, and the rig crew
    /// (Serafin) could not be reached at all. Rows, top to bottom: each drawn
    /// crew button in the window's own order, grouped by its spacer heading
    /// ("// LOCAL", "// THE RIG"); the slot summary; then the window's buttons.
    ///
    /// Truth comes from each button's FSM state (decoded live): Unselected,
    /// Slot N Equip (picked), Slot N LOCKED (mandatory crew), Blocked (not
    /// available), CREW FULL (transient refusal). Enter on a crew row clicks
    /// the button's own SELECT, which the FSM turns into equip or unequip.
    /// Backspace stays the map's Back (GameQueries.MapBack).</summary>
    internal static class CrewSelectTable
    {
        private enum Kind { Crew, Slots, Button }

        private sealed class Row
        {
            public Kind Kind;
            public Transform Crew;   // the "Crew Button ..." container
            public GameObject Target; // its SELECT, or a window button
            public int Section;
            public string Group;
        }

        private static List<Row> _rows = new List<Row>();
        private static readonly List<string> _groups = new List<string>();

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Rebuild(),
            Cols = row => 1,
            RowSpeech = (r, c) => RowRead(r),
            CellSpeech = (r, c) => RowRead(r),
            SectionOf = row => row >= 0 && row < _rows.Count ? _rows[row].Section : 0,
            SectionPrefix = s => s >= 0 && s < _groups.Count && _groups[s].Length > 0
                ? _groups[s] + "." : "",
            EmptyRow = () => Lex.T("crew.table-empty"),
            EmptyCol = () => Lex.T("crew.table-empty"),
            EmptyDetail = () => Lex.T("crew.table-empty"),
            EmptyCommit = () => Lex.T("crew.table-empty"),
            Source = "crew",
        };

        static CrewSelectTable()
        {
            Table.Detail = (row, col) => Table.Say(RowRead(row));
            Table.Commit = (row, col) => Commit(row);
        }

        // ---------- Window ----------

        /// <summary>The crew window when it is the topmost map sub-window (the
        /// Crew Confrim box overlays it at the next stage).</summary>
        private static Transform Window()
        {
            var sub = GameQueries.MapSubWindow();
            return sub != null && sub.name == "Crew Window" ? sub : null;
        }

        public static bool Active() => Window() != null;

        private static int Rebuild()
        {
            _rows = new List<Row>();
            _groups.Clear();
            var w = Window();
            if (w == null) return 0;
            var buttons = w.Find("Crew Buttons");
            int section = 0;
            string group = null;
            if (buttons != null)
            {
                foreach (Transform c in buttons)
                {
                    if (!c.gameObject.activeInHierarchy || !Util.RenderedUp(c)) continue;
                    if (c.name.StartsWith("Spacer"))
                    {
                        group = GroupName(c);
                        if (group != null) { _groups.Add(group); section = _groups.Count - 1; }
                        continue;
                    }
                    if (!c.name.StartsWith("Crew Button")) continue;
                    var select = c.Find("SELECT");
                    if (select == null || !select.gameObject.activeInHierarchy) continue;
                    if (_groups.Count == 0) { _groups.Add(""); section = 0; }
                    _rows.Add(new Row { Kind = Kind.Crew, Crew = c, Target = select.gameObject,
                                        Section = section, Group = group });
                }
            }
            // Slots and window buttons ride their own unnamed section.
            _groups.Add("");
            int tail = _groups.Count - 1;
            _rows.Add(new Row { Kind = Kind.Slots, Section = tail });
            foreach (var name in new[] { "CONFIRM", "BACK" })
            {
                var b = w.Find(name);
                if (b == null || !b.gameObject.activeInHierarchy || !Util.RenderedUp(b)) continue;
                _rows.Add(new Row { Kind = Kind.Button, Target = b.gameObject, Section = tail });
            }
            return _rows.Count;
        }

        private static string GroupName(Transform spacer)
        {
            string text = FocusPatch.RenderedChildText(spacer, "Name");
            if (text == null) return null;
            text = text.Trim().TrimStart('/').Trim();
            return text.Length > 0 ? text : null;
        }

        // ---------- Reads ----------

        private static string State(Transform crew)
        {
            var fsm = crew != null ? crew.GetComponent<PlayMakerFSM>() : null;
            return fsm != null ? fsm.ActiveStateName ?? "" : "";
        }

        private static string NameOf(Transform crew)
        {
            string name = FocusPatch.RenderedChildText(crew, "Name");
            if (name != null) return name;
            // Blocked members hide their Display (and with it the Name).
            var fsm = crew.GetComponent<PlayMakerFSM>();
            var id = fsm != null ? fsm.FsmVariables.GetFsmString("Crew Member") : null;
            return id != null && !string.IsNullOrEmpty(id.Value) ? id.Value : crew.name;
        }

        /// <summary>0 = in no slot; 1 or 2 = the slot it fills.</summary>
        private static int SlotOf(string state)
        {
            if (state.StartsWith("Slot 1") && (state.EndsWith("Equip") || state.EndsWith("LOCKED"))) return 1;
            if (state.StartsWith("Slot 2") && (state.EndsWith("Equip") || state.EndsWith("LOCKED"))) return 2;
            return 0;
        }

        private static string StatusOf(Row row)
        {
            string state = State(row.Crew);
            int slot = SlotOf(state);
            if (slot > 0 && state.EndsWith("LOCKED"))
                return Lex.T("crew.locked-in") + " " + slot + ".";
            if (slot > 0)
                return Lex.T("crew.selected-slot").Replace("{n}", slot.ToString());
            if (state == "Blocked")
            {
                string label = Describe.FirstText(row.Target);
                return (label ?? Lex.T("crew.not-available")) + ".";
            }
            return Lex.T("crew.available");
        }

        private static string RowRead(int row)
        {
            if (row < 0 || row >= _rows.Count) return Lex.T("crew.table-empty");
            var r = _rows[row];
            switch (r.Kind)
            {
                case Kind.Crew:
                    return NameOf(r.Crew) + ". " + StatusOf(r) + FocusPatch.CrewChips(r.Crew);
                case Kind.Slots:
                    return SlotsRead();
                default:
                    string label = Describe.FirstText(r.Target) ?? r.Target.name;
                    return label + " " + Lex.T("crew.button") + ".";
            }
        }

        private static string SlotsRead()
        {
            string s1 = null, s2 = null;
            foreach (var r in _rows)
            {
                if (r.Kind != Kind.Crew) continue;
                int slot = SlotOf(State(r.Crew));
                if (slot == 1) s1 = NameOf(r.Crew);
                else if (slot == 2) s2 = NameOf(r.Crew);
            }
            string empty = Lex.T("crew.slot-empty");
            return Lex.T("crew.slots") + " 1, " + (s1 ?? empty) + ". 2, " + (s2 ?? empty) + ".";
        }

        // ---------- Keys ----------

        public static bool HandleKeys()
        {
            if (!Active()) { _open = false; return false; }
            return Table.HandleKeys();
        }

        private static void Commit(int row)
        {
            var r = _rows[row];
            switch (r.Kind)
            {
                case Kind.Slots:
                    Table.Say(SlotsRead());
                    return;
                case Kind.Button:
                    Navigator.Click(r.Target);
                    return;
            }
            string state = State(r.Crew);
            if (state == "Blocked" || state.EndsWith("LOCKED"))
            {
                Table.Say(StatusOf(r)); // the game ignores the press; say why
                return;
            }
            _pendingCrew = r.Crew;
            _pendingWasSlot = SlotOf(state);
            _pendingAt = Time.unscaledTime + 0.35f;
            Navigator.Click(r.Target);
        }

        // ---------- Open line and press results ----------

        private static bool _open;
        private static float _openAt;
        private static Transform _pendingCrew;
        private static int _pendingWasSlot;
        private static float _pendingAt;

        public static void Tick()
        {
            bool active = Active();
            if (active && !_open)
            {
                _open = true;
                _openAt = Time.unscaledTime + 0.5f; // let the button FSMs settle
                Table.Reset();
            }
            else if (!active)
            {
                _open = false;
                _openAt = 0f;
                _pendingCrew = null;
                return;
            }

            if (_openAt > 0f && Time.unscaledTime >= _openAt)
            {
                _openAt = 0f;
                int count = Rebuild();
                Table.Reset();
                if (count > 0)
                {
                    string group = _rows[0].Group != null ? _rows[0].Group + ". " : "";
                    SpeechService.Say(Lex.T("crew.table-title") + " " + SlotsRead() + " "
                        + Lex.T("crew.table-hint") + " " + group + RowRead(0),
                        Priority.Immediate, "crew");
                }
            }

            if (_pendingCrew != null && Time.unscaledTime >= _pendingAt)
            {
                var crew = _pendingCrew;
                _pendingCrew = null;
                int now = SlotOf(State(crew));
                string line;
                if (now > 0 && _pendingWasSlot == 0)
                    line = NameOf(crew) + " " + Lex.T("crew.selected-slot").Replace("{n}", now.ToString());
                else if (now == 0 && _pendingWasSlot > 0)
                    line = NameOf(crew) + " " + Lex.T("crew.removed");
                else if (now == 0)
                    line = Lex.T("crew.no-free-slot");
                else
                    line = NameOf(crew) + ". " + Lex.T("crew.selected-slot").Replace("{n}", now.ToString());
                Rebuild();
                SpeechService.Say(line + " " + SlotsRead(), Priority.Immediate, "crew");
            }
        }
    }
}
