using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Middleware
{
    /// <summary>
    /// The drive journal as a table — the CS1 JournalTable ported on decode D5
    /// (2026-07-31): the window root path, tab row, heading templates, and the
    /// TRACKING/ABANDON rendered labels all carry byte-identical; the QuestLog
    /// API is the row truth every press (stateless reads); the game's own nav
    /// soup is never walked. PRESS-ONLY (owner ruling): movement reads, Enter
    /// performs the cell's full game-sanctioned action via native clicks.
    ///
    /// CS2 deltas: the window ships NO native abandon confirmation (D5:
    /// abandonPopup None — abandon is a checkpoint rollback) — the mod adds a
    /// two-step Enter confirm; open/tab truth reads the framework window
    /// component directly; wording is Lex-keyed.
    /// </summary>
    internal static class JournalTable
    {
        private static readonly string[] HeaderKeys =
            { "journal.col.name", "journal.col.objectives",
              "journal.col.track", "journal.col.abandon" };

        private static readonly TableEngine T = new TableEngine
        {
            Rows = () => Rows().Count,
            Cols = _ => HeaderKeys.Length,
            RowSpeech = (row, col) =>
            {
                var rows = Rows();
                return col <= 0 ? RowReport(rows[row])
                    : rows[row] + ". " + CellText(rows[row], col);
            },
            CellSpeech = (row, col) => Lex.T(HeaderKeys[col]) + ": " + CellText(Rows()[row], col),
            OnRowArrive = (prev, row) =>
            {
                // Owner ruling (CS1 session 11): the highlighted row IS the
                // expanded row — expand on arrival, close the one departed.
                var rows = Rows();
                prev = Mathf.Clamp(prev, 0, rows.Count - 1);
                if (row != prev) SyncExpansion(rows[prev], rows[row]);
                else EnsureExpanded(rows[row]);
            },
            OnColArrive = (row, col, delta) => { if (delta > 0) EnsureExpanded(Rows()[row]); },
            Detail = (row, _) => FullRow(row),
            // Enter on Name/Objectives = the full-row re-read (owner ruling
            // 2026-08-02: expansion is automatic on arrival — no toggle click,
            // no "Expanded." chatter); Track/Abandon keep their actions.
            Commit = (row, col) =>
            {
                if (col <= 1) { FullRow(row); return; }
                Activate(Rows()[row], col);
            },
            EmptyRow = () => Lex.T("journal.empty"),
            EmptyCol = () => Lex.T("journal.empty"),
            EmptyDetail = () => Lex.T("journal.empty"),
            Source = "journal",
        };

        // ---------- Open truth (the framework window component, D5) ----------

        private static QuestLogWindow _window;
        private static float _windowSearchAt = -10f;

        /// <summary>The search walks every loaded object, so without a window (title,
        /// loading) it is retried once a second, not every frame (2026-10-25).</summary>
        public static bool WindowOpen()
        {
            if (_window == null && Time.unscaledTime - _windowSearchAt >= 1f)
            {
                _windowSearchAt = Time.unscaledTime;
                foreach (var w in Resources.FindObjectsOfTypeAll<QuestLogWindow>())
                    if (w != null && w.gameObject.scene.IsValid()) { _window = w; break; }
            }
            return _window != null && _window.isOpen;
        }

        public static void InvalidateScene()
        {
            _window = null;
            _windowSearchAt = -10f;
            _driveLogButton = null;
        }

        private static bool ShowingActive
            => _window == null || _window.isShowingActiveQuests;

        /// <summary>J / Backspace: the Drive Log Button FSM's own Open event —
        /// works both directions (CS1 finding; D5 confirms Idle/Open/Close).</summary>
        private static PlayMakerFSM _driveLogButton;

        public static void Toggle()
        {
            if (_driveLogButton == null || !_driveLogButton.gameObject.scene.IsValid())
            {
                _driveLogButton = null;
                foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.gameObject == null) continue;
                    if (!fsm.gameObject.scene.IsValid()) continue;
                    if (fsm.gameObject.name != "Drive Log Button") continue;
                    _driveLogButton = fsm;
                    break;
                }
            }
            if (_driveLogButton != null) { _driveLogButton.SendEvent("Open"); return; }
            Plugin.Log.LogWarning("[Journal] Drive Log Button FSM not found");
        }

        public static bool HandleKeys()
        {
            // Slash = the native tab swap (structural button names, D5).
            // The destination tab announces itself by its OWN rendered label
            // (owner ruling 2026-08-02 — the swap was silent).
            if (Input.GetKeyDown(KeyCode.Slash))
            {
                var tab = FindByName(ShowingActive ? "Completed Button" : "Active Button");
                if (tab != null)
                {
                    var label = tab.GetComponentInChildren<TMPro.TMP_Text>(false);
                    string name = label != null ? SpeechService.Clean(label.text) : null;
                    Navigator.Click(tab.gameObject);
                    // Row level: the first arrow reads the first drive (2026-10-25).
                    T.Reset(-1);
                    if (!string.IsNullOrEmpty(name))
                        SpeechService.Say(name + ".", Priority.Immediate, "journal");
                    else
                        Plugin.Log.LogWarning("[Journal] tab has no rendered label — swap silent");
                    return true;
                }
                Plugin.Log.LogWarning("[Journal] tab button not found");
                return true;
            }
            return T.HandleKeys();
        }

        public static void OnWindowClosed()
        {
            T.Reset(-1);
            _confirmQuest = null;
            _pendingTrackQuest = null;
            _announceTrackingAt = -1f;
            SpeechService.Say(Lex.T("journal.closed"), Priority.Immediate, "journal");
        }

        /// <summary>Open (2026-10-25): it used to be silent and parked the cursor on
        /// the first drive unread, so the first Down skipped it. Now it names the
        /// window and its tab, and the first arrow reads the first drive.</summary>
        public static void OnWindowOpened()
        {
            T.Reset(-1);
            _confirmQuest = null;
            string tab = null;
            var tabButton = FindByName(ShowingActive ? "Active Button" : "Completed Button");
            var label = tabButton != null ? tabButton.GetComponentInChildren<TMP_Text>(false) : null;
            if (label != null) tab = SpeechService.Clean(label.text);
            int n = Rows().Count;
            SpeechService.Say(Lex.T("journal.open") + (tab != null ? " " + tab + "." : "")
                + " " + (n == 0 ? Lex.T("journal.empty") : n + (n == 1 ? Lex.T("journal.count-one") : Lex.T("journal.count"))),
                Priority.Immediate, "journal");
        }

        // ---------- Rows (stateless — the QuestLog is the truth every press) ----------

        private static List<string> Rows()
        {
            var rows = new List<string>();
            try
            {
                var states = ShowingActive ? QuestState.Active
                                           : QuestState.Success | QuestState.Failure;
                foreach (var q in QuestLog.GetAllQuests(states)) rows.Add(q);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[Journal] quests: " + e.Message); }
            return rows;
        }

        private static void SyncExpansion(string prevQuest, string quest)
        {
            EnsureExpanded(quest);
            if (prevQuest == quest) return;
            if (FindRowActionButton(prevQuest, "TRACKING") == null) return; // already closed
            var heading = FindHeadingButton(prevQuest);
            if (heading != null) Navigator.Click(heading.gameObject);
        }

        /// <summary>Pull the quest's row out if it isn't already (the tracking
        /// toggle renders only inside an expanded row — its presence IS the
        /// expanded test). Silent: the cell read that follows announces.</summary>
        private static void EnsureExpanded(string quest)
        {
            if (FindRowActionButton(quest, "TRACKING") != null) return;
            var heading = FindHeadingButton(quest);
            if (heading != null) Navigator.Click(heading.gameObject);
        }

        private static void FullRow(int row)
        {
            var rows = Rows();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < HeaderKeys.Length; i++)
                sb.Append(Lex.T(HeaderKeys[i])).Append(": ")
                  .Append(CellText(rows[row], i)).Append(' ');
            SpeechService.Say(sb.ToString().TrimEnd(), Priority.Immediate, "journal");
        }

        private static string RowReport(string quest)
        {
            bool tracked = false;
            try { tracked = QuestLog.IsQuestTrackingEnabled(quest); } catch { }
            return quest + (tracked ? ", " + Lex.T("journal.tracked") : ".");
        }

        private static string CellText(string quest, int col)
        {
            switch (col)
            {
                case 0: return RowReport(quest);
                case 1: return ObjectivesText(quest);
                case 2:
                    try
                    {
                        return QuestLog.IsQuestTrackingEnabled(quest)
                            ? Lex.T("journal.tracked-cell") : Lex.T("journal.not-tracked");
                    }
                    catch { return Lex.T("journal.not-tracked"); }
                default:
                    try
                    {
                        return QuestLog.IsQuestAbandonable(quest)
                            ? Lex.T("journal.abandon") : Lex.T("journal.no-abandon");
                    }
                    catch { return Lex.T("journal.no-abandon"); }
            }
        }

        private static string ObjectivesText(string quest)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string desc = QuestLog.GetQuestDescription(quest);
                if (!string.IsNullOrEmpty(desc))
                    Append(sb, SpeechService.Clean(desc));
                int n = QuestLog.GetQuestEntryCount(quest);
                for (int i = 1; i <= n; i++)
                {
                    var st = QuestLog.GetQuestEntryState(quest, i);
                    if (st == QuestState.Unassigned) continue;
                    string text = SpeechService.Clean(QuestLog.GetQuestEntry(quest, i));
                    if (string.IsNullOrEmpty(text)) continue;
                    if (text.Trim('-', ' ').Length == 0) continue; // dash placeholders
                    if (st == QuestState.Success)
                        text = text.TrimEnd('.') + Lex.T("journal.done");
                    else if (st == QuestState.Failure)
                        text = text.TrimEnd('.') + Lex.T("journal.failed");
                    Append(sb, text);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[Journal] entries: " + e.Message); }
            return sb.Length > 0 ? sb.ToString() : Lex.T("journal.no-objectives");
        }

        private static void Append(System.Text.StringBuilder sb, string part)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(part);
            if (!part.EndsWith(".") && !part.EndsWith("!") && !part.EndsWith("?")) sb.Append('.');
        }

        // ---------- Enter: the cell's game-sanctioned action (press-only) ----------

        private static void Activate(string quest, int col)
        {
            // Cols 0/1 never reach here (Commit re-reads the row instead —
            // owner ruling 2026-08-02: arrival owns expansion; the heading
            // click and its "Expanded." notification are gone).
            switch (col)
            {
                case 2: ToggleTracking(quest); return;
                default: Abandon(quest); return;
            }
        }

        private static void ToggleTracking(string quest)
        {
            EnsureExpanded(quest);
            var toggle = FindRowActionButton(quest, "TRACKING");
            if (toggle == null)
            {
                // The pull-out can render a beat late — retry once (CS1 finding).
                _pendingTrackQuest = quest;
                _pendingTrackAt = Time.unscaledTime + 0.35f;
                return;
            }
            Navigator.Click(toggle.gameObject);
            // The click sets tracking synchronously (QuestLogWindow), so the result
            // is read on the next tick instead of a quarter second later.
            _announceTrackingAt = Time.unscaledTime;
        }

        private static float _announceTrackingAt = -1f;
        private static string _pendingTrackQuest;
        private static float _pendingTrackAt = -1f;

        private static bool _wasOpen;

        public static void Tick()
        {
            bool open = WindowOpen();
            if (_wasOpen && !open) OnWindowClosed();
            else if (!_wasOpen && open) OnWindowOpened();
            _wasOpen = open;

            if (_pendingTrackQuest != null && Time.unscaledTime >= _pendingTrackAt)
            {
                string quest = _pendingTrackQuest;
                _pendingTrackQuest = null;
                var toggle = FindRowActionButton(quest, "TRACKING");
                if (toggle != null)
                {
                    Navigator.Click(toggle.gameObject);
                    _announceTrackingAt = Time.unscaledTime;
                }
                else
                    SpeechService.Say(Lex.T("journal.track-unavailable"),
                        Priority.Immediate, "journal");
            }

            if (_announceTrackingAt < 0 || Time.unscaledTime < _announceTrackingAt) return;
            _announceTrackingAt = -1f;
            // The RESULT from the API the pips poll (single tracking: one name).
            string tracked = null;
            try
            {
                foreach (var q in QuestLog.GetAllQuests(QuestState.Active))
                    if (QuestLog.IsQuestTrackingEnabled(q)) { tracked = q; break; }
            }
            catch { }
            SpeechService.Say(tracked != null
                    ? Lex.T("journal.tracking-prefix") + tracked + "."
                    : Lex.T("journal.tracking-none"),
                Priority.Immediate, "journal");
        }

        // ---------- Abandon: MOD-SIDE two-step confirm (CS2 delta, D5: the game
        // ships no confirmation and abandon is a checkpoint rollback) ----------

        private static string _confirmQuest;
        private static float _confirmUntil = -1f;

        private static void Abandon(string quest)
        {
            bool abandonable = false;
            try { abandonable = QuestLog.IsQuestAbandonable(quest); } catch { }
            if (!abandonable)
            {
                SpeechService.Say(Lex.T("journal.no-abandon"), Priority.Immediate, "journal");
                return;
            }
            if (_confirmQuest != quest || Time.unscaledTime > _confirmUntil)
            {
                _confirmQuest = quest;
                _confirmUntil = Time.unscaledTime + 6f;
                SpeechService.Say(Lex.T("journal.abandon-confirm"), Priority.Immediate, "journal");
                return;
            }
            _confirmQuest = null;
            // The row is already expanded (arrival expands it); clicking its heading
            // again closed it and hid ABANDON, so the confirm did nothing
            // (2026-10-25). Make sure it is open, then press its own ABANDON.
            EnsureExpanded(quest);
            var abandonBtn = FindRowActionButton(quest, "ABANDON") ?? FindWindowButton("ABANDON");
            if (abandonBtn != null) Navigator.Click(abandonBtn.gameObject);
            else
            {
                Plugin.Log.LogInfo("[Journal] abandon button not found.");
                SpeechService.Say(Lex.T("journal.abandon-unavailable"), Priority.Immediate, "journal");
            }
        }

        // ---------- Native-object resolution (rendered labels, graceful silence) ----------

        private static Transform WindowRoot()
        {
            var go = GameObject.Find("Letterbox Canvas/Drive System/CS Drive Log");
            return go != null ? go.transform : null;
        }

        private static Transform FindByName(string name)
        {
            var root = WindowRoot();
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(false))
                if (t.name == name) return t;
            return null;
        }

        private static Button FindHeadingButton(string quest)
        {
            var root = WindowRoot();
            if (root == null) return null;
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                var tmp = b.GetComponentInChildren<TMP_Text>(false);
                if (tmp != null && string.Equals(tmp.text?.Trim(), quest,
                        System.StringComparison.OrdinalIgnoreCase))
                    return b;
            }
            return null;
        }

        private static Button FindRowActionButton(string quest, string labelFragment)
        {
            var heading = FindHeadingButton(quest);
            if (heading == null) return null;
            var templateRoot = heading.transform.parent;
            if (templateRoot == null) return null;
            foreach (var b in templateRoot.GetComponentsInChildren<Button>(false))
            {
                var tmp = b.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null && tmp.text != null
                    && tmp.text.ToUpperInvariant().Contains(labelFragment))
                    return b;
            }
            return null;
        }

        private static Button FindWindowButton(string labelFragment)
        {
            var root = WindowRoot();
            if (root == null) return null;
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                var tmp = b.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null && tmp.text != null
                    && tmp.text.ToUpperInvariant().Contains(labelFragment))
                    return b;
            }
            return null;
        }
    }
}
