using System;
using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The top-bar table — V (owner ruling, Checkpoint A + 2026-07-31:
    /// ONE cohesive walkable table over the rendered top bar, ALL buttons
    /// included and committable with Enter; per-render structure). Rows are
    /// providers over the live render: the Vitals channels speak their current
    /// readout, the dice tray speaks per-die value/state (D11 reads), and the
    /// bar's buttons — Ship (RIG/EXPLORE), MAP, Leave/Continue — commit as one
    /// native click each (the same click a pointer would deliver; MAP's click is
    /// the decoded SendEvent Open, D8). Rows that are not currently rendered do
    /// not list (alpha-honest).
    ///
    /// Grammar: V enters (and exits), Up/Down walk rows, Enter commits button
    /// rows (value rows repeat), Backspace exits. An excursion over whatever
    /// table owns the floor beneath — that table's cursor is untouched.</summary>
    internal static class TopBarTable
    {
        private sealed class Row
        {
            public Func<string> Speech;          // null/empty = row not rendered now
            public Func<GameObject> Commit;      // null = readout row
        }

        public static bool Entered { get; private set; }

        private static readonly List<Row> Registry = new List<Row>();
        private static readonly List<Row> Live = new List<Row>();
        private static float _builtAt = -1f;

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Rows().Count,
            Cols = _ => 1,
            RowSpeech = (r, c) => Speak(r),
            CellSpeech = (r, c) => Speak(r),
            Detail = (r, c) => Table.Say(Speak(r)),
            Commit = (r, c) => DoCommit(r),
            EmptyRow = () => Lex.T("topbar.empty"),
            EmptyCommit = () => Lex.T("topbar.empty"),
            Source = "query",
        };

        public static void Init()
        {
            // Vitals channels — one row each, current readout (render-clock truth).
            for (int i = 0; i < 10; i++)
            {
                int index = i;
                Registry.Add(new Row { Speech = () => VitalsLine(index) });
            }

            // Dice tray — one row per rendered die (D11 read idiom).
            for (int i = 1; i <= 5; i++)
            {
                int n = i;
                Registry.Add(new Row { Speech = () => DieLine(n) });
            }

            // Buttons — rendered label + role, Enter clicks the game's own button.
            Registry.Add(new Row
            {
                Speech = () => ButtonLine(GameQueries.ShipToggleButton()),
                Commit = GameQueries.ShipToggleButton,
            });
            Registry.Add(new Row
            {
                Speech = () => ButtonLine(FindButton(
                    "Letterbox Canvas/Top UI/Ship and Map Buttons/Map UI/Button")),
                Commit = () => FindButton(
                    "Letterbox Canvas/Top UI/Ship and Map Buttons/Map UI/Button"),
            });
            Registry.Add(new Row
            {
                Speech = () => ButtonLine(LeaveButton()),
                Commit = LeaveButton,
            });
        }

        public static bool HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.V))
            {
                if (Entered) { Exit(); return true; }
                // V only engages on the floors where the bar renders (CS1 KeyScope
                // idiom: out-of-scope keys stay dead, never half-work).
                var mode = ModeModel.Current();
                if (mode != Mode.Station && mode != Mode.RigRooms
                    && mode != Mode.ActionView) return false;
                Enter();
                return true;
            }
            if (!Entered) return false;
            if (Input.GetKeyDown(KeyCode.Backspace)) { Exit(); return true; }
            return Table.HandleKeys();
        }

        public static void Tick()
        {
            // The bar itself is only rendered on gameplay floors; leaving them
            // (title, travel teardown) drops the excursion.
            if (!Entered) return;
            var mode = ModeModel.Current();
            if (mode == Mode.Title || mode == Mode.Travel) Exit();
        }

        private static void Enter()
        {
            Entered = true;
            _builtAt = -1f;
            Table.Reset();
            if (Rows().Count == 0)
            {
                Table.Say(Lex.T("topbar.empty"));
                Exit();
                return;
            }
            Table.Say(Lex.T("topbar.title") + " " + Speak(0));
        }

        private static void Exit()
        {
            Entered = false;
            SpeechService.Say(Lex.T("topbar.closed"), Priority.Immediate, "query");
        }

        // ---------- Rows ----------

        private static List<Row> Rows()
        {
            if (Time.unscaledTime - _builtAt > 0.4f)
            {
                Live.Clear();
                foreach (var row in Registry)
                {
                    string s;
                    try { s = row.Speech(); }
                    catch { s = null; }
                    if (!string.IsNullOrEmpty(s)) Live.Add(row);
                }
                _builtAt = Time.unscaledTime;
            }
            return Live;
        }

        private static string Speak(int row)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("topbar.empty");
            return rows[row].Speech() ?? Lex.T("topbar.empty");
        }

        private static void DoCommit(int row)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return;
            var target = rows[row].Commit != null ? rows[row].Commit() : null;
            if (target == null)
            {
                Table.Say(Speak(row)); // readout row: Enter repeats (CS1 idiom)
                return;
            }
            Exit();
            Navigator.Click(target);
        }

        private static string VitalsLine(int index)
        {
            var lines = Vitals.Read();
            return index < lines.Count ? lines[index] : null;
        }

        /// <summary>Per-die row from the tray's own Die FSM (DiceValue face; 9 IS
        /// the glitched render; literal Used/Broken states — D11 §6).</summary>
        private static string DieLine(int n)
        {
            var go = GameObject.Find("Letterbox Canvas/Top UI/Dice UI/Dice Slot " + n + "/Die");
            if (go == null || !go.activeInHierarchy) return null;
            var fsm = go.GetComponent<PlayMakerFSM>();
            if (fsm == null) return null;
            var sb = new System.Text.StringBuilder(Lex.T("dice.die")).Append(' ').Append(n);
            string state = fsm.ActiveStateName ?? "";
            var value = fsm.FsmVariables.GetFsmFloat("DiceValue");
            if (state == "Used") sb.Append(", ").Append(Lex.T("dice.spent"));
            else if (state == "Broken") sb.Append(", ").Append(Lex.T("dice.broken"));
            else if (value != null)
                sb.Append(", ").Append(value.Value == 9f
                    ? Lex.T("dice.glitched") : value.Value.ToString("0"));
            sb.Append('.');
            return sb.ToString();
        }

        private static string ButtonLine(GameObject button)
        {
            if (button == null || !button.activeInHierarchy) return null;
            if (Util.AlphaUpTo(button.transform) < 0.05f) return null;
            // Label renders on the button's parent block (Name sibling) or itself.
            string label = Describe.FirstText(button.transform.parent != null
                ? button.transform.parent.gameObject : button);
            if (string.IsNullOrEmpty(label)) return null;
            return label + Lex.T("topbar.button-suffix");
        }

        private static GameObject FindButton(string path)
        {
            var go = GameObject.Find(path);
            return go != null && go.activeInHierarchy ? go : null;
        }

        private static GameObject LeaveButton()
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmGameObject("Leave Button");
            return v != null && v.Value != null && v.Value.activeInHierarchy ? v.Value : null;
        }
    }
}
