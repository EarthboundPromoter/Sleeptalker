using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Tutorial modals (owner design 2026-07-26): a composed, walkable
    /// buffer on the TableEngine in single-column form. On appearance (real focus
    /// enters the Tutorial System — the game moves it to the box's CONTINUE button,
    /// gamepad-gated) the buffer builds — "Tutorial: topic", one block per Message
    /// paragraph, "CONTINUE button" last — and every block enqueues for a hands-free
    /// read; Up/Down interrupt and walk blocks, Left/Right repeat the block, Enter
    /// dismisses from anywhere (native focus is genuinely on CONTINUE; dismissal is
    /// low-stakes by ruling). Bypassing the game's glyph switcher toward keyboard
    /// variants was REJECTED (owner + evidence 2026-07-26): the PC variant speaks
    /// native mouse/keyboard controls (still not our keys) and mixed dials threaten
    /// the gamepad-gated focus machinery — we transcode the Gamepad variant instead.</summary>
    internal static class TutorialReader
    {
        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => _blocks.Count,
            Cols = _ => 1,
            RowSpeech = (r, c) => _blocks[r],
            CellSpeech = (r, c) => _blocks[r],
            EmptyRow = () => "No tutorial text.",
            EmptyCol = () => "No tutorial text.",
            Source = "tutorial",
        };

        private static readonly List<string> _blocks = new List<string>();
        private static int _panelId;

        /// <summary>Focus inside the Tutorial System = the modal owns the surface.</summary>
        public static bool Up()
        {
            var focused = Navigator.Current();
            return focused != null && Util.HasAncestor(focused, "Tutorial System");
        }

        /// <summary>From Plugin.Update: detect appearance (focus-in on a new panel),
        /// build the buffer, enqueue the hands-free read.</summary>
        public static void Tick()
        {
            if (!Up())
            {
                _panelId = 0;
                return;
            }
            var panel = PanelOf(Navigator.Current());
            if (panel == null) return;
            int id = panel.GetInstanceID();
            if (id == _panelId) return;
            _panelId = id;

            Build(panel);
            Table.Reset();
            foreach (var block in _blocks)
                SpeechService.Say(block, Priority.Queued, "tutorial");
        }

        public static void Move(int delta) => Table.MoveRow(delta);
        public static void Repeat() => Table.MoveCol(0);

        private static Transform PanelOf(GameObject go)
        {
            for (var cur = go.transform; cur != null; cur = cur.parent)
                if (cur.parent != null && cur.parent.name == "Tutorial System")
                    return cur;
            return null;
        }

        private static void Build(Transform panel)
        {
            _blocks.Clear();

            var topicNode = panel.Find("Label Box");
            string topic = topicNode != null ? CleanBlock(TmpDeep(topicNode)) : null;
            _blocks.Add(topic != null ? "Tutorial: " + topic.TrimStart('/') + "." : "Tutorial.");

            var message = panel.Find("Message");
            var tmp = message != null ? message.GetComponent<TMP_Text>() : null;
            if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            {
                foreach (var para in Regex.Split(tmp.text, @"\n\s*\n"))
                {
                    string block = CleanBlock(para);
                    if (!string.IsNullOrEmpty(block)) _blocks.Add(block);
                }
            }

            var button = panel.Find("Button");
            string label = button != null ? CleanBlock(TmpDeep(button)) : null;
            if (label != null) _blocks.Add(label + " button.");
        }

        // ---------- Glyph transcode seam ----------

        /// <summary>Sprite-glyph -> mod-key vocabulary. EMPTY until the first glyph
        /// tutorial is captured live (dice tutorials expected) — every unknown tag
        /// speaks a placeholder and logs loudly so it lands in the wart/glyph
        /// registry instead of vanishing. Transcode-not-strip discipline.</summary>
        private static readonly Dictionary<string, string> GlyphWords =
            new Dictionary<string, string>();

        private static readonly Regex SpriteTag =
            new Regex("<sprite[^>]*?(?:name=\"([^\"]+)\")?[^>]*>", RegexOptions.Compiled);

        private static string CleanBlock(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string transcoded = SpriteTag.Replace(raw, m =>
            {
                string name = m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                if (GlyphWords.TryGetValue(name, out var word)) return " " + word + " ";
                Plugin.Log.LogWarning("[Tutorial] UNMAPPED GLYPH: " + m.Value);
                return " (glyph) ";
            });
            return SpeechService.Clean(transcoded);
        }

        private static string TmpDeep(Transform t)
        {
            var tmp = t.GetComponentInChildren<TMP_Text>(false);
            return tmp != null ? tmp.text : null;
        }
    }
}
