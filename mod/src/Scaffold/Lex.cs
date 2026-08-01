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
                { "prompt.scroll-keys", "W and S" },
                { "prompt.rotate-keys", "A and D" },
                { "prompt.unknown", "(glyph)" },
                // Cancel resolution
                { "cancel.none", "No back path here." },
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
                { "zone.disabled", "Disabled." },
                { "zone.col.name", "Name" },
                { "zone.col.clock", "Clock" },
                { "zone.col.drives", "Drives" },
                { "zone.col.actions", "Actions" },
                { "zone.col.description", "Description" },
                { "zone.clock.shown", "shown" },
                { "zone.clock.none", "none." },
                { "zone.drives.none", "none." },
                { "zone.actions.none", "none." },
                { "zone.actions.one", "action" },
                { "zone.actions.many", "actions" },
                { "zone.actions.unavailable", "unavailable" },
                { "zone.desc.none", "none." },
                // Location (action view) table
                { "loc.section.actions", "Action cards." },
                { "loc.section.clocks", "Clock cards." },
                { "loc.empty", "Nothing here." },
                { "loc.none", "none." },
                { "loc.takes-die", "takes a die" },
                { "loc.card-disabled", "Action card disabled." },
                { "loc.col.name", "Name" },
                { "loc.col.requires", "Requires" },
                { "loc.col.risk", "Risk" },
                { "loc.col.cost", "Cost" },
                { "loc.col.predicted", "Predicted" },
                { "loc.col.narrative", "Narrative" },
                { "loc.col.progress", "Progress" },
                // Dice allocation
                { "dice.picker", "Die picker." },
                { "dice.picker-closed", "Picker closed." },
                { "dice.refused", "Die unavailable." },
                { "dice.slotted", "Die slotted." },
                { "dice.returned", "Die returned." },
                { "dice.die", "Die" },
                { "dice.die-lower", "die" },
                { "dice.spent", "spent" },
                { "dice.broken", "broken" },
                { "dice.glitched", "glitched" },
                // Top-bar table
                { "topbar.title", "Top bar." },
                { "topbar.closed", "Closed." },
                { "topbar.empty", "Top bar not available." },
                { "topbar.button-suffix", " button." },
                // Modes (F1)
                { "mode.title", "Title screen" },
                { "mode.pause", "Pause menu" },
                { "mode.tutorial", "Tutorial" },
                { "mode.conversation", "Conversation" },
                { "mode.diceallocation", "Die picker" },
                { "mode.map", "Map" },
                { "mode.cycletransition", "Cycle transition" },
                { "mode.actionview", "Location" },
                { "mode.travel", "Traveling" },
                { "mode.rigrooms", "Rig" },
                { "mode.station", "Station" },
                // F1 key summaries
                { "help.table", "Arrows walk the table, Left and Right browse facets, Space full row, Enter activates, Backspace goes back, V top bar." },
                { "help.topbar", "Up and Down walk the bar, Enter presses buttons, V or Backspace closes." },
                { "help.tutorial", "Up and Down walk the text, Enter continues." },
                { "help.conversation", "Numbers pick choices, Up and Down move, Enter confirms, B reads the log." },
                { "help.dice", "Arrows move between dice, Enter picks, Backspace takes back or closes." },
                { "help.map", "Arrows move between markers, Enter selects, Backspace closes the map." },
                { "help.native", "Arrows and Enter, game controls." },
                // Vitals
                { "vitals.stress", "Stress" },
                { "vitals.energy", "Energy" },
                { "vitals.fuel", "Fuel" },
                { "vitals.supplies", "Supplies" },
                { "vitals.cryo", "Cryo" },
                { "vitals.glitch", "Glitch" },
                { "vitals.permaglitch", "Permanent glitch" },
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
