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
        private struct Cell
        {
            public string Speech;
            public GameObject Target;   // null = readout cell
        }

        public static bool Entered { get; private set; }

        // Three fixed rows (owner ruling 2026-07-31): vitals | buttons | dice.
        private const int RowVitals = 0, RowButtons = 1, RowDice = 2;
        private static readonly List<Cell> CellCache = new List<Cell>();
        private static int _cachedRow = -1;
        private static float _builtAt = -1f;

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => 3,
            Cols = row => Cells(row).Count,
            RowSpeech = (r, c) => RowLine(r),
            CellSpeech = (r, c) => CellLine(r, c),
            Detail = (r, c) => Table.Say(RowLine(r)),
            Commit = (r, c) => DoCommit(r, c),
            EmptyRow = () => Lex.T("topbar.empty"),
            EmptyCol = () => Lex.T("topbar.empty"),
            EmptyCommit = () => Lex.T("topbar.empty"),
            Source = "query",
            // The CS1 UI-bar idiom: cells start on the first Left/Right; the
            // column resets on every row switch.
            ColReset = -1,
            ResetColOnRowChange = true,
        };

        public static void Init() { }

        private static readonly string[] RowKeys =
            { "topbar.row.vitals", "topbar.row.buttons", "topbar.row.dice" };

        private static List<Cell> Cells(int row)
        {
            if (_cachedRow == row && Time.unscaledTime - _builtAt <= 0.4f)
                return CellCache;
            _cachedRow = row;
            _builtAt = Time.unscaledTime;
            CellCache.Clear();
            switch (row)
            {
                case RowVitals:
                    foreach (var line in Vitals.Read())
                        CellCache.Add(new Cell { Speech = line });
                    break;
                case RowButtons:
                    AddButton(GameQueries.ShipToggleButton());
                    AddButton(FindButton(
                        "Letterbox Canvas/Top UI/Ship and Map Buttons/Map UI/Button"));
                    // Contextual third cell: the Leave/Continue button when the
                    // bar renders one (flagged to owner — strike if unwanted).
                    AddButton(LeaveButton());
                    break;
                case RowDice:
                    for (int n = 1; n <= 5; n++)
                    {
                        string line = DieLine(n);
                        if (line != null) CellCache.Add(new Cell { Speech = line });
                    }
                    break;
            }
            return CellCache;
        }

        private static void AddButton(GameObject button)
        {
            string line = ButtonLine(button);
            if (line != null) CellCache.Add(new Cell { Speech = line, Target = button });
        }

        /// <summary>Row arrival: the row's name, then every cell joined — the CS1
        /// full-row read.</summary>
        private static string RowLine(int row)
        {
            var cells = Cells(row);
            if (cells.Count == 0)
                return Lex.T(RowKeys[Mathf.Clamp(row, 0, 2)]) + " " + Lex.T("topbar.row-empty");
            var sb = new System.Text.StringBuilder(Lex.T(RowKeys[Mathf.Clamp(row, 0, 2)]));
            foreach (var cell in cells) sb.Append(' ').Append(cell.Speech);
            return sb.ToString();
        }

        private static string CellLine(int row, int col)
        {
            var cells = Cells(row);
            if (col < 0 || col >= cells.Count) return Lex.T("topbar.empty");
            return cells[col].Speech;
        }

        private static void DoCommit(int row, int col)
        {
            var cells = Cells(row);
            if (col < 0 || col >= cells.Count)
            {
                Table.Say(RowLine(row)); // row level: repeat, never a blind click
                return;
            }
            var target = cells[col].Target;
            if (target == null)
            {
                Table.Say(cells[col].Speech); // readout cell: Enter repeats
                return;
            }
            Exit(false); // the click's own consequences speak
            Navigator.Click(target);
        }

        /// <summary>The floors this excursion may sit on. Anything else claiming
        /// the mode (pause, map, dice, conversation, tutorial, cycle) evicts it
        /// SILENTLY and immediately — the excursion must never outlive its floor
        /// (sync review HIGH-1: an entered V-table under pause kept the keys and
        /// could click into paused FSMs).</summary>
        private static bool FloorLive()
        {
            var mode = ModeModel.Current();
            return mode == Mode.Station || mode == Mode.RigRooms || mode == Mode.ActionView;
        }

        public static bool HandleKeys()
        {
            if (Entered && !FloorLive())
            {
                Exit(false);
                return false; // the new floor's owner sees this key untouched
            }
            if (Input.GetKeyDown(KeyCode.V))
            {
                if (Entered) { Exit(true); return true; }
                // V only engages on the floors where the bar renders (CS1 KeyScope
                // idiom: out-of-scope keys stay dead, never half-work).
                if (!FloorLive()) return false;
                Enter();
                return true;
            }
            if (!Entered) return false;
            if (Input.GetKeyDown(KeyCode.Backspace)) { Exit(true); return true; }
            return Table.HandleKeys();
        }

        public static void Tick()
        {
            if (Entered && !FloorLive()) Exit(false);
        }

        private static void Enter()
        {
            Entered = true;
            _builtAt = -1f;
            _cachedRow = -1;
            Table.Reset();
            Table.Say(Lex.T("topbar.title") + " " + RowLine(RowVitals));
        }

        private static void Exit(bool announce)
        {
            Entered = false;
            if (announce)
                SpeechService.Say(Lex.T("topbar.closed"), Priority.Immediate, "query");
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
