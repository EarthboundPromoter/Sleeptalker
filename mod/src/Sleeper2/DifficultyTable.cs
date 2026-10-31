using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The new-game difficulty screen as a list (owner request
    /// 2026-10-30). The game's spatial navigation jumped between the three
    /// cards, the TUTORIALS toggle and CONFIRM in no fixed order. Rows, top to
    /// bottom: the tutorials toggle, SAFE, RISKY, DANGEROUS, CONFIRM
    /// DIFFICULTY, BACK.
    ///
    /// Anatomy (decoded live): MAIN MENU/Demo Menu/Difficulty Menu, whose own
    /// FSM state names the chosen difficulty (SAFE / RISKY / DANGEROUS); each
    /// card under BUTTONS has Name, Description and a SELECT button that sends
    /// the menu FSM its event; "Tutorials" is a toggle button with an FSM in
    /// ON / OFF; "Choose Difficulty" is CONFIRM. Enter clicks those same
    /// buttons. Backspace stays the title's Back (TitleFlow).</summary>
    internal static class DifficultyTable
    {
        private enum Kind { Tutorials, Difficulty, Button }

        private sealed class Row
        {
            public Kind Kind;
            public Transform Card;    // difficulty card ("SAFE Button")
            public GameObject Target; // what Enter clicks
        }

        private static List<Row> _rows = new List<Row>();

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Rebuild(),
            Cols = row => 1,
            RowSpeech = (r, c) => RowRead(r),
            CellSpeech = (r, c) => RowRead(r),
            SectionOf = row => 0,
            SectionPrefix = s => "",
            EmptyRow = () => Lex.T("diff.empty"),
            EmptyCol = () => Lex.T("diff.empty"),
            EmptyDetail = () => Lex.T("diff.empty"),
            EmptyCommit = () => Lex.T("diff.empty"),
            Source = "difficulty",
        };

        static DifficultyTable()
        {
            Table.Detail = (row, col) => Table.Say(RowRead(row));
            Table.Commit = (row, col) => Commit(row);
        }

        private static Transform Menu()
        {
            var go = GameObject.Find("MAIN MENU/Demo Menu/Difficulty Menu");
            if (go == null || !go.activeInHierarchy || !Util.RenderedUp(go.transform)) return null;
            return go.transform;
        }

        public static bool Active() => Menu() != null;

        private static GameObject Live(Transform root, string path)
        {
            var t = root.Find(path);
            return t != null && t.gameObject.activeInHierarchy && Util.RenderedUp(t) ? t.gameObject : null;
        }

        private static int Rebuild()
        {
            _rows = new List<Row>();
            var menu = Menu();
            if (menu == null) return 0;
            var tut = Live(menu, "Tutorials");
            if (tut != null) _rows.Add(new Row { Kind = Kind.Tutorials, Target = tut });
            var buttons = menu.Find("BUTTONS");
            if (buttons != null)
            {
                foreach (Transform card in buttons) // the game's own order: SAFE, RISKY, DANGEROUS
                {
                    if (!card.gameObject.activeInHierarchy || !Util.RenderedUp(card)) continue;
                    var select = Live(card, "SELECT");
                    if (select == null) continue;
                    _rows.Add(new Row { Kind = Kind.Difficulty, Card = card, Target = select });
                }
            }
            foreach (var name in new[] { "Choose Difficulty", "BACK" })
            {
                var b = Live(menu, name);
                if (b != null) _rows.Add(new Row { Kind = Kind.Button, Target = b });
            }
            return _rows.Count;
        }

        // ---------- Reads ----------

        private static string Chosen()
        {
            var menu = Menu();
            var fsm = menu != null ? menu.GetComponent<PlayMakerFSM>() : null;
            return fsm != null ? fsm.ActiveStateName ?? "" : "";
        }

        private static string CardName(Transform card)
            => FocusPatch.RenderedChildText(card, "Name") ?? card.name.Replace(" Button", "");

        private static string CardDescription(Transform card)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Transform c in card)
            {
                if (!c.name.StartsWith("Description")) continue;
                if (!c.gameObject.activeInHierarchy || !Util.RenderedUp(c)) continue;
                var tmp = c.GetComponent<TMPro.TMP_Text>();
                if (tmp == null) continue;
                foreach (var raw in tmp.text.Split('\n'))
                {
                    string line = SpeechService.Clean(raw.Trim().TrimStart('-').Trim());
                    if (string.IsNullOrEmpty(line)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(line);
                    if (!line.EndsWith(".")) sb.Append('.');
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string RowRead(int row)
        {
            if (row < 0 || row >= _rows.Count) return Lex.T("diff.empty");
            var r = _rows[row];
            switch (r.Kind)
            {
                case Kind.Tutorials:
                {
                    string label = Describe.FirstText(r.Target) ?? "TUTORIALS";
                    var menu = Menu();
                    bool recommended = menu != null && Live(menu, "Recommended ON") != null;
                    return label + (recommended ? ", " + Lex.T("diff.recommended") : "") + ". "
                        + Lex.T("diff.toggle");
                }
                case Kind.Difficulty:
                {
                    string name = CardName(r.Card);
                    var sb = new System.Text.StringBuilder(name).Append('.');
                    if (Chosen() == name) sb.Append(' ').Append(Lex.T("diff.chosen"));
                    string desc = CardDescription(r.Card);
                    if (desc != null) sb.Append(' ').Append(desc);
                    return sb.ToString();
                }
                default:
                    return (Describe.FirstText(r.Target) ?? r.Target.name) + " " + Lex.T("crew.button") + ".";
            }
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
            Navigator.Click(r.Target);
            if (r.Kind == Kind.Button) return; // CONFIRM / BACK speak through their own screens
            _pendingRow = row;
            _pendingAt = Time.unscaledTime + 0.3f;
        }

        // ---------- Open line and press results ----------

        private static bool _open;
        private static float _openAt;
        private static int _pendingRow = -1;
        private static float _pendingAt;

        public static void Tick()
        {
            bool active = Active();
            if (!active)
            {
                _open = false;
                _openAt = 0f;
                _pendingRow = -1;
                return;
            }
            if (!_open)
            {
                _open = true;
                _openAt = Time.unscaledTime + 0.4f;
            }
            if (_openAt > 0f && Time.unscaledTime >= _openAt)
            {
                _openAt = 0f;
                int count = Rebuild();
                Table.Reset();
                var menu = Menu();
                string note = menu != null ? FocusPatch.RenderedChildText(menu, "Difficulty Note") : null;
                string chosen = Chosen();
                var sb = new System.Text.StringBuilder(Lex.T("diff.title"));
                if (!string.IsNullOrEmpty(chosen))
                    sb.Append(' ').Append(Lex.T("diff.now-chosen")).Append(' ').Append(chosen).Append('.');
                if (note != null) sb.Append(' ').Append(note).Append('.');
                sb.Append(' ').Append(Lex.T("diff.hint"));
                if (count > 0) sb.Append(' ').Append(RowRead(0));
                SpeechService.Say(sb.ToString(), Priority.Immediate, "difficulty");
            }
            if (_pendingRow >= 0 && Time.unscaledTime >= _pendingAt)
            {
                int row = _pendingRow;
                _pendingRow = -1;
                Rebuild();
                if (row >= _rows.Count) return;
                var r = _rows[row];
                string line = r.Kind == Kind.Difficulty
                    ? CardName(r.Card) + " " + Lex.T("diff.chosen-now")
                    : (Describe.FirstText(r.Target) ?? "") + ".";
                SpeechService.Say(line, Priority.Immediate, "difficulty");
            }
        }
    }
}
