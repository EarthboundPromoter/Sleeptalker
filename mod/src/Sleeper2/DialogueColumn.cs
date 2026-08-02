using UnityEngine;
using UnityEngine.UI;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The unified dialogue column (owner design 2026-08-01, decode
    /// D19): one seamless scrollback — history rows flowing into the live
    /// frontier (Continue, or the response choices with their odds) with no
    /// barrier and no review key. Up from the frontier walks back through
    /// DialogueState.History (the mod transcript; block[i] ≡ History[i] by
    /// construction — the game's own blocks carry bare text, speaker backfill
    /// is ours) and scrolls the native window with the cursor (plain
    /// ScrollRect, BottomToTop, 0 = newest; no virtualization, D19). Down
    /// returns to the frontier and hands the keys back to the SHIPPED native
    /// grammar — response selection, odds reads, number picks, Enter — which
    /// this class never re-implements. Enter on a history row re-reads it
    /// (owner ruling 2; no native action exists there). A new line snaps the
    /// cursor home to the frontier (the live edge always follows).</summary>
    internal static class DialogueColumn
    {
        private static int _cursor = -1;   // -1 = at the frontier (native keys)
        private static int _lastCount = -1;

        public static bool InHistory => _cursor >= 0;

        /// <summary>New line arrived: the live edge follows (silent snap).</summary>
        public static void Tick()
        {
            if (!ConversationEvents.ConversationActive)
            {
                _cursor = -1;
                _lastCount = -1;
                return;
            }
            int count = DialogueState.History.Count;
            if (_lastCount >= 0 && count != _lastCount && _cursor >= 0)
            {
                _cursor = -1;
                ScrollTo(1f, toBottom: true);
            }
            _lastCount = count;
        }

        /// <summary>Up: from the frontier into history at the newest row, then
        /// back one row per press. Consumes the key iff history exists.</summary>
        public static bool Up()
        {
            int count = DialogueState.History.Count;
            if (count == 0) return false;
            if (_cursor < 0) _cursor = count - 1;
            else if (_cursor > 0) _cursor--;
            SpeakRow(_cursor);
            return true;
        }

        /// <summary>Down: forward through history; stepping past the newest row
        /// returns to the frontier (native selection resumes, window snaps to
        /// the bottom). Consumed only while in history.</summary>
        public static bool Down()
        {
            if (_cursor < 0) return false;
            int count = DialogueState.History.Count;
            if (_cursor < count - 1)
            {
                _cursor++;
                SpeakRow(_cursor);
                return true;
            }
            _cursor = -1;
            ScrollTo(1f, toBottom: true);
            SpeechService.Say(Lex.T(DialogueState.MenuOpen
                ? "dlg.frontier-choices" : "dlg.frontier"), Priority.Immediate, "nav");
            return true;
        }

        /// <summary>Enter on a history row: re-read (a press is never silent;
        /// owner ruling 2). Not consumed at the frontier — native commit runs.</summary>
        public static bool EnterKey()
        {
            if (_cursor < 0) return false;
            SpeakRow(_cursor);
            return true;
        }

        private static void SpeakRow(int index)
        {
            var history = DialogueState.History;
            if (index < 0 || index >= history.Count) return;
            var line = history[index];
            string speaker = string.IsNullOrEmpty(line.Speaker)
                ? null : line.Speaker;
            SpeechService.Say(
                (speaker != null ? speaker + ": " : "") + line.Text,
                Priority.Immediate, "nav");
            // Window sync: blocks align to history by construction; newest sits
            // at normalized 0 (BottomToTop). Approximate row position; the
            // typewriter/gamepad-mode contention windows are logged in D19.
            int count = history.Count;
            float pos = count > 1 ? 1f - (index / (float)(count - 1)) : 0f;
            ScrollTo(pos, toBottom: false);
        }

        private static void ScrollTo(float normalized, bool toBottom)
        {
            var content = GameObject.Find("Scroll Content");
            var scroll = content != null
                ? content.GetComponentInParent<ScrollRect>() : null;
            if (scroll == null) return;
            scroll.verticalNormalizedPosition = toBottom ? 0f : normalized;
        }
    }
}
