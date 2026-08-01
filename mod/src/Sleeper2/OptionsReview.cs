using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Options menu review — the CS1 OptionsReview port on decode D7
    /// (2026-08-01). The port is near-verbatim: the panel path is IDENTICAL
    /// (PAUSE/Pause Canvas/Options Menu), the row anatomy carries (row object
    /// holds the localized label; value buttons are its children), and CS2
    /// simplifies to one flat screen, no tabs. Rows: TEXT (Default/Large),
    /// SCROLL (Slower/Default/Faster), MUSIC (0–5), SFX (0–5), Back.
    ///
    /// Idiom (CS1 owner ruling, carried): Up/Down between rows speaking the
    /// rendered row label + current value; Left/Right MUTATE on value rows —
    /// native click of the neighboring value button, auto-apply (the game
    /// recolors the accent labels immediately, and D7's Set Up pre-sync means
    /// the accents are true on open); Enter engages only Back (the native
    /// Force Save Options persistence commit).
    ///
    /// Current value = the accent-colored value label (render-first law: the
    /// accent IS where the screen draws "current"; colors logged once per open
    /// for palette calibration). The D7 Lua table (TextSize, SCROLL_MULTIPLYER,
    /// MUSIC, SFX — SFX stored INVERTED: shown 5 = Lua 0) rides as a loud
    /// cross-check log only, never the speech source.</summary>
    internal static class OptionsReview
    {
        private static GameObject _panel;
        private static int _row;
        private static bool _wasActive;
        private static bool _colorsLogged;
        private static bool _mismatchLogged;

        public static bool IsActive()
        {
            if (_panel == null || !_panel.activeInHierarchy)
                _panel = GameObject.Find("PAUSE/Pause Canvas/Options Menu");
            bool active = _panel != null && _panel.activeInHierarchy;
            if (active && !_wasActive)
            {
                _row = 0;
                _colorsLogged = false;
                _mismatchLogged = false;
                SpeechService.Say(Lex.T("options.intro"), Priority.Queued, "nav");
            }
            _wasActive = active;
            return active;
        }

        public static void Review(int direction)
        {
            var rows = Rows();
            if (rows.Count == 0) return;
            _row = Mathf.Clamp(_row + direction, 0, rows.Count - 1);
            SpeakRow(rows[_row]);
        }

        /// <summary>Left/Right on a value row: native click of the neighboring
        /// value button — the commit path the game itself uses (onClick →
        /// SendEvent into the PAUSE FSM); applies live.</summary>
        public static void Adjust(int direction)
        {
            var rows = Rows();
            if (rows.Count == 0) return;
            var row = rows[_row];
            if (row.Values.Count == 0)
            {
                SpeakRow(row); // Back row: nothing to adjust
                return;
            }
            int current = CurrentValueIndex(row);
            int target = current < 0
                ? (direction > 0 ? 0 : row.Values.Count - 1)
                : Mathf.Clamp(current + direction, 0, row.Values.Count - 1);
            if (target == current)
            {
                SpeakRow(row); // at the end — restate
                return;
            }
            var button = row.Values[target];
            if (button != null && button.interactable)
            {
                Navigator.Click(button.gameObject);
                SpeechService.Say(Label(button) ?? Lex.T("options.changed"),
                    Priority.Immediate, "nav");
            }
        }

        /// <summary>Enter: engage only where it leads onward (Back). Value rows
        /// restate — Left/Right is the mutation grammar.</summary>
        public static bool Activate()
        {
            var rows = Rows();
            if (rows.Count == 0) return false;
            var row = rows[_row];
            if (row.Values.Count == 0 && row.Self != null)
            {
                Navigator.Click(row.Self.gameObject);
                return true;
            }
            SpeakRow(row);
            return true;
        }

        // ---------- Structure (D7 (c): row = Options Menu child; label on the
        // row object; value Buttons are its children; Back is itself a Button) ----------

        private sealed class Row
        {
            public string Name;            // row GameObject name (structural; cross-check key)
            public string LabelText;       // rendered localized label
            public Button Self;            // Back-style rows: the row IS a button
            public readonly List<Button> Values = new List<Button>();
        }

        private static List<Row> Rows()
        {
            var rows = new List<Row>();
            if (_panel == null) return rows;
            foreach (Transform child in _panel.transform)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                var row = new Row { Name = child.name };
                row.Self = child.GetComponent<Button>();
                var ownTmp = child.GetComponent<TMP_Text>();
                if (ownTmp != null) row.LabelText = SpeechService.Clean(ownTmp.text);
                else
                {
                    var childTmp = child.GetComponentInChildren<TMP_Text>(false);
                    if (childTmp != null) row.LabelText = SpeechService.Clean(childTmp.text);
                }
                if (row.Self == null)
                {
                    foreach (Transform v in child)
                    {
                        var b = v.GetComponent<Button>();
                        if (b != null) row.Values.Add(b);
                    }
                }
                if (row.Self != null || row.Values.Count > 0)
                    rows.Add(row);
            }
            return rows;
        }

        private static void SpeakRow(Row row)
        {
            if (row.Values.Count == 0)
            {
                SpeechService.Say((row.LabelText ?? row.Name)
                    + Lex.T("topbar.button-suffix"), Priority.Immediate, "nav");
                return;
            }
            int current = CurrentValueIndex(row);
            CrossCheck(row, current);
            string value = current >= 0 ? Label(row.Values[current]) : null;
            SpeechService.Say((row.LabelText ?? Lex.T("options.setting"))
                + (value != null ? ", " + value : "") + ".",
                Priority.Immediate, "nav");
        }

        /// <summary>The value whose label wears the accent color — the rendered
        /// current-value marker sighted players read (CS1 screenshot oracle;
        /// literal transcode over a closed set). Colors logged once per open:
        /// if CS2's accent palette defeats the redness score, the log shows it
        /// and the score recalibrates from evidence.</summary>
        private static int CurrentValueIndex(Row row)
        {
            int best = -1;
            float bestScore = 0.12f; // below this nothing is confidently accented
            for (int i = 0; i < row.Values.Count; i++)
            {
                var tmp = row.Values[i].GetComponentInChildren<TMP_Text>(false);
                if (tmp == null) continue;
                Color c = tmp.color;
                float score = c.r - (c.g + c.b) / 2f;
                if (!_colorsLogged)
                    Plugin.Log.LogInfo("[Options] " + (row.Name ?? "?") + "/"
                        + (tmp.text != null ? tmp.text.Trim() : "?")
                        + " color=" + c + " score=" + score.ToString("F3"));
                if (score > bestScore) { bestScore = score; best = i; }
            }
            _colorsLogged = true;
            return best;
        }

        /// <summary>Loud render-vs-Lua cross-check (D7 table; log only, never
        /// speech): TEXT→TextSize 0/1, SCROLL→SCROLL_MULTIPLYER 100/400/600,
        /// MUSIC→MUSIC 0–5 direct, SFX→SFX INVERTED (shown 5 = Lua 0).</summary>
        private static void CrossCheck(Row row, int renderIndex)
        {
            if (_mismatchLogged || renderIndex < 0) return;
            int expected = -1;
            var lua = LuaStore.Num(
                row.Name == "TEXT" ? "TextSize"
                : row.Name == "SCROLL" ? "SCROLL_MULTIPLYER"
                : row.Name == "MUSIC" ? "MUSIC"
                : row.Name == "SFX" ? "SFX" : null);
            if (!lua.HasValue) return;
            switch (row.Name)
            {
                case "TEXT": expected = lua.Value >= 1 ? 1 : 0; break;
                case "SCROLL":
                    expected = lua.Value <= 100 ? 0 : lua.Value <= 400 ? 1 : 2; break;
                case "MUSIC": expected = (int)lua.Value; break;
                case "SFX": expected = 5 - (int)lua.Value; break;
            }
            if (expected >= 0 && expected != renderIndex)
            {
                _mismatchLogged = true;
                Plugin.Log.LogWarning("[Options] render/Lua MISMATCH on " + row.Name
                    + ": accent index " + renderIndex + " vs Lua-derived " + expected
                    + " — recalibrate the accent score or the D7 mapping");
            }
        }

        private static string Label(Button b)
        {
            var tmp = b != null ? b.GetComponentInChildren<TMP_Text>(false) : null;
            return tmp != null ? SpeechService.Clean(tmp.text) : null;
        }
    }
}
