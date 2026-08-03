using UnityEngine;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>F1 — the invisible key table (owner design 2026-08-03,
    /// supersedes the prose SpeakHelp read): one row per binding, key in the
    /// left column, function in the right, the standard table grammar.
    /// F1/Backspace close per convention; any key outside the grammar closes
    /// silently and falls through to its owner — an invisible reference
    /// sheet must never become a key trap. Rows come from Lex
    /// ("helpkeys.*", "K|function;K|function") so wording stays one-place
    /// editable.</summary>
    internal static class HelpTable
    {
        public static bool Open { get; private set; }

        private static string[][] _entries = new string[0][];
        private static Mode _mode;

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => _entries.Length,
            Cols = row => 2,
            RowSpeech = (r, c) => RowRead(r),
            CellSpeech = (r, c) => CellRead(r, c),
            SectionOf = row => 0,
            SectionPrefix = s => "",
            EmptyRow = () => Lex.T("help.empty"),
            EmptyCol = () => Lex.T("help.empty"),
            EmptyDetail = () => Lex.T("help.empty"),
            EmptyCommit = () => Lex.T("help.empty"),
            Source = "help",
        };

        static HelpTable()
        {
            Table.Detail = (row, col) => Table.Say(RowRead(row));
            Table.Commit = (row, col) => Table.Say(RowRead(row)); // nothing to activate
        }

        public static void Toggle()
        {
            if (Open) { Close(); return; }
            _mode = ModeModel.Current();
            _entries = EntriesFor(_mode);
            Open = true;
            Table.Reset();
            // Composed open: title + the first row (Reset parks ON row 0 —
            // an unspoken first row would otherwise be unreachable).
            SpeechService.Say(Lex.T("help.title")
                + (_entries.Length > 0 ? " " + RowRead(0) : ""),
                Priority.Immediate, "help");
        }

        private static void Close()
        {
            Open = false;
            SpeechService.Say(Lex.T("help.closed"), Priority.Immediate, "help");
        }

        /// <summary>Owns the keys while open. False = not open, or the sheet
        /// just retired silently under a foreign key (which then falls
        /// through to its real owner).</summary>
        public static bool HandleKeys()
        {
            if (!Open) return false;
            // The surface underneath changed (window closed, mode swap):
            // the sheet is stale — retire silently.
            if (ModeModel.Current() != _mode) { Open = false; return false; }
            if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.Backspace))
            {
                Close();
                return true;
            }
            if (Table.HandleKeys()) return true;
            if (Input.anyKeyDown) Open = false;
            return false;
        }

        private static string RowRead(int row)
        {
            if (row < 0 || row >= _entries.Length) return Lex.T("help.empty");
            return _entries[row][0] + ", " + _entries[row][1] + ".";
        }

        private static string CellRead(int row, int col)
        {
            if (row < 0 || row >= _entries.Length) return Lex.T("help.empty");
            return _entries[row][col <= 0 ? 0 : 1] + ".";
        }

        /// <summary>The mode's rows from its Lex sheet — same selector the
        /// prose help used (pause splits by the active review).</summary>
        private static string[][] EntriesFor(Mode mode)
        {
            string key;
            switch (mode)
            {
                case Mode.Station:
                case Mode.RigRooms:
                case Mode.ActionView:
                    key = TopBarTable.Entered ? "helpkeys.topbar" : "helpkeys.table";
                    break;
                case Mode.Tutorial: key = "helpkeys.tutorial"; break;
                case Mode.Conversation: key = "helpkeys.conversation"; break;
                case Mode.DiceAllocation: key = "helpkeys.dice"; break;
                case Mode.Map: key = "helpkeys.map"; break;
                case Mode.Character: key = "helpkeys.character"; break;
                case Mode.DriveLog: key = "helpkeys.journal"; break;
                case Mode.Inventory: key = "helpkeys.inventory"; break;
                case Mode.Pause:
                    key = OptionsReview.IsActive() ? "helpkeys.options"
                        : GuideReader.IsActive() ? "helpkeys.guide" : "helpkeys.pause";
                    break;
                default: key = "helpkeys.native"; break;
            }
            return Parse(Lex.T(key));
        }

        private static string[][] Parse(string sheet)
        {
            var rows = sheet.Split(';');
            var entries = new string[rows.Length][];
            for (int i = 0; i < rows.Length; i++)
            {
                int bar = rows[i].IndexOf('|');
                entries[i] = bar >= 0
                    ? new[] { rows[i].Substring(0, bar), rows[i].Substring(bar + 1) }
                    : new[] { rows[i], "" };
            }
            return entries;
        }
    }
}
