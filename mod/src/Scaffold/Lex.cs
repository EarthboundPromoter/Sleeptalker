using System;
using System.Collections.Generic;
using System.IO;

namespace Sleeptalker.Scaffold
{
    /// <summary>Mod speech lexicon (owner direction 2026-07-26): every mod-authored
    /// word or phrase routes through one keyed table so mod output is localizable
    /// without touching call sites. Embedded English defaults; an optional override
    /// file (BepInEx/config/Sleeptalker.lex.txt, "key = value" lines, # comments)
    /// re-words or translates any entry. Game text never passes through here —
    /// rendered game text is already localized by the game and is spoken as-is.
    /// New call sites must use Lex.T; pre-existing inline literals migrate in the
    /// localization sweep (flagged: carousel class words, odds regex).</summary>
    internal static class Lex
    {
        private static readonly Dictionary<string, string> Default =
            new Dictionary<string, string>
            {
                // Tutorial surfaces
                { "tutorial.button-suffix", " button." },
                { "tutorial.still-open", "Tutorial still open." },
                { "tutorial.shown-with", "Shown with" },
                { "tutorial.and", " and " },
                { "tutorial.empty", "Tutorial." },
                // Prompt glyphs -> the MOD's keys (B2 ruling, CS1 2026-07-21,
                // carried to CS2 by owner confirmation 2026-07-26)
                { "prompt.enter", "Enter" },
                { "prompt.backspace", "Backspace" },
                { "prompt.arrows", "the arrow keys" },
                { "prompt.unknown", "(glyph)" },
                // Skill-check odds and resolution
                { "odds.percent", "percent" },
                { "odds.positive", "positive" },
                { "odds.neutral", "neutral" },
                { "odds.negative", "negative" },
                { "check.positive", "Positive." },
                { "check.neutral", "Neutral." },
                { "check.negative", "Negative." },
                { "check.stress-up", "Stress up." },
                { "check.stress-down", "Stress down." },
                // Zone table
                { "zone.empty", "No locations found." },
                { "zone.new", "New." },
                // Vitals
                { "vitals.stress", "Stress" },
                { "vitals.up", "up" },
                { "vitals.down", "down" },
                { "vitals.of", "of" },
            };

        private static Dictionary<string, string> _override;

        public static void Init()
        {
            try
            {
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "Sleeptalker.lex.txt");
                if (!File.Exists(path)) return;
                var map = new Dictionary<string, string>();
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                _override = map;
                Plugin.Log.LogInfo("[Lex] " + map.Count + " override(s) loaded.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Lex] override file unreadable: " + e.Message);
            }
        }

        /// <summary>The spoken form for a lexicon key. A missing key logs loudly and
        /// speaks the key itself — audible, never silent.</summary>
        public static string T(string key)
        {
            if (_override != null && _override.TryGetValue(key, out var o)) return o;
            if (Default.TryGetValue(key, out var d)) return d;
            Plugin.Log.LogWarning("[Lex] MISSING KEY: " + key);
            return key;
        }
    }
}
