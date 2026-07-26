using System;
using UnityEngine;

namespace Sleeptalker.Scaffold
{
    /// <summary>
    /// The shared 2-D table mechanism (audit A1/E1; owner ruling 2026-07-26:
    /// COMPOSITION, not inheritance). One instance per table surface; the table
    /// stays a static class and wires providers once. The engine owns cursor
    /// state, clamping, the standard key map, and the announce FLOW; every
    /// spoken string comes from a provider, so wording blocks stay per-surface
    /// and this class stays game-agnostic.
    ///
    /// The flow, distilled from the six live tables (their behavior is the
    /// spec — golden-transcript verified):
    ///   MoveRow: start hook -> fetch fresh count -> empty speech or clamp ->
    ///            section crossing (prefix + column reset) -> arrive hook
    ///            (pre-speech: expansion, camera-sync) -> RowSpeech(row, col)
    ///            -> after hook (post-speech: camera contract).
    ///   MoveCol: fetch -> empty speech or clamp row+col (sentinel-aware) ->
    ///            re-derive section silently -> arrive hook -> CellSpeech.
    ///   Space/Enter: fetch -> optional empty speech -> clamp -> delegate.
    /// </summary>
    internal sealed class TableEngine
    {
        // ---------- Providers (wired once at composition) ----------

        /// <summary>Row count, fetched fresh per keypress (live-truth rule).</summary>
        public Func<int> Rows;
        /// <summary>Column count for a row (sections may carry different columns).</summary>
        public Func<int, int> Cols;
        /// <summary>Row-arrival read, column-aware: tables keep their own
        /// "(col == 0 ? full row : name + cell)" composition and wording.</summary>
        public Func<int, int, string> RowSpeech;
        /// <summary>Column-arrival read ("Header: cell", self-labeled cells, ...).</summary>
        public Func<int, int, string> CellSpeech;
        /// <summary>Space. Receives the clamped position.</summary>
        public Action<int, int> Detail;
        /// <summary>Enter. Receives the clamped position. Null = key not consumed.</summary>
        public Action<int, int> Commit;

        /// <summary>Spoken when MoveRow finds no rows; null = silent.</summary>
        public Func<string> EmptyRow;
        /// <summary>Spoken when MoveCol finds no rows; null = silent.</summary>
        public Func<string> EmptyCol;
        /// <summary>Spoken when Detail finds no rows; null = silent.</summary>
        public Func<string> EmptyDetail;
        /// <summary>Spoken when Commit finds no rows; null = silent.</summary>
        public Func<string> EmptyCommit;

        // Sections (stacked grids): identity per row, crossing announces and
        // resets the column. Null SectionOf = single-section table.
        public Func<int, int> SectionOf;
        public Func<int, string> SectionPrefix;
        public int SectionReset;

        // Hooks (null = none). OnRowMoveStart: before anything (commit
        // withdrawal). OnRowArrive/OnColArrive: pre-speech (expansion pull-out,
        // camera-synced browse). AfterRowSpeech: post-speech (camera contract:
        // write only when the row actually changed - the table compares).
        public Action OnRowMoveStart;
        public Action<int, int> OnRowArrive;      // (prevRow, row)
        public Action<int, int> AfterRowSpeech;   // (prevRow, row)
        public Action<int, int, int> OnColArrive; // (row, col, delta)

        /// <summary>Speech source tag ("table"; the UI bar review uses "query").</summary>
        public string Source = "table";
        /// <summary>Column the cursor rests on after a reset. -1 = row-level
        /// sentinel (UI bar idiom: cells start on the first Left/Right).</summary>
        public int ColReset;
        /// <summary>Reset the column on every actual row change (UI bar idiom);
        /// default false: the column survives row moves (grid idiom).</summary>
        public bool ResetColOnRowChange;

        // ---------- Cursor (tables may set directly on entry/rebuild) ----------

        public int Row;
        public int Col;
        private int _section;

        public void Reset(int row = 0)
        {
            Row = row;
            Col = ColReset;
            _section = SectionReset;
        }

        // ---------- Key dispatch (the mod-wide table grammar) ----------

        public bool HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.DownArrow)) { MoveRow(1); return true; }
            if (Input.GetKeyDown(KeyCode.UpArrow)) { MoveRow(-1); return true; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { MoveCol(1); return true; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { MoveCol(-1); return true; }
            if (Input.GetKeyDown(KeyCode.Space)) { DoDetail(); return true; }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                && Commit != null)
            { DoCommit(); return true; }
            return false;
        }

        // ---------- Movement ----------

        public void MoveRow(int delta)
        {
            OnRowMoveStart?.Invoke();
            int count = Rows();
            if (count == 0) { SpeakOpt(EmptyRow); return; }
            int prev = Row;
            Row = Mathf.Clamp(Row + delta, 0, count - 1);
            if (ResetColOnRowChange && Row != prev) Col = ColReset;
            string prefix = "";
            if (SectionOf != null)
            {
                int section = SectionOf(Row);
                if (section != _section)
                {
                    // Boundary crossing: announce the section, reset the column.
                    _section = section;
                    Col = ColReset;
                    prefix = SectionPrefix(section) + " ";
                }
            }
            OnRowArrive?.Invoke(prev, Row);
            Say(prefix + RowSpeech(Row, Col));
            AfterRowSpeech?.Invoke(prev, Row);
        }

        public void MoveCol(int delta)
        {
            int count = Rows();
            if (count == 0) { SpeakOpt(EmptyCol); return; }
            Row = Mathf.Clamp(Row, 0, count - 1);
            if (SectionOf != null) _section = SectionOf(Row); // silent re-derive
            int cols = Cols(Row);
            if (cols == 0) { SpeakOpt(EmptyCol); return; }
            // Sentinel-aware: from row level (-1) the first move lands on the
            // first cell in either direction; otherwise plain clamp.
            Col = Mathf.Clamp(Col < 0 ? 0 : Col + delta, 0, cols - 1);
            OnColArrive?.Invoke(Row, Col, delta);
            Say(CellSpeech(Row, Col));
        }

        // ---------- Space / Enter ----------

        private void DoDetail()
        {
            int count = Rows();
            if (count == 0) { SpeakOpt(EmptyDetail); return; }
            Row = Mathf.Clamp(Row, 0, count - 1);
            Detail?.Invoke(Row, Col);
        }

        private void DoCommit()
        {
            int count = Rows();
            if (count == 0) { SpeakOpt(EmptyCommit); return; }
            Row = Mathf.Clamp(Row, 0, count - 1);
            Commit(Row, Col);
        }

        // ---------- Speech ----------

        public void Say(string text)
            => SpeechService.Say(text, Priority.Immediate, Source);

        private void SpeakOpt(Func<string> provider)
        {
            string text = provider?.Invoke();
            if (!string.IsNullOrEmpty(text)) Say(text);
        }
    }
}
