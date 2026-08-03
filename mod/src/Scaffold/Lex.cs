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
                // Dialogue column
                { "dlg.frontier", "Latest. Enter continues." },
                { "dlg.frontier-choices", "Choices." },
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
                { "zone.col.drives", "Tracked drive" },
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
                { "zone.section.rooms", "Rooms." },
                { "zone.section.ops", "Ship operations." },
                // Map table
                { "map.empty", "No markers here." },
                { "map.here", "You are here." },
                { "map.title", "Map." },
                { "map.closed", "Map closed." },
                { "map.plane.belt", "Belt view." },
                { "map.plane.contracts", " contracts." },
                { "map.section.suffix", " sector." },
                { "map.section.controls", "Map controls." },
                { "map.section.unknown", "Unmapped" },
                { "map.blocked", "Blocked." },
                { "map.blocked.fuel", "Not enough fuel." },
                { "map.requires", "Requires" },
                { "map.fuel-unit", "fuel." },
                { "map.col.fuel", "Fuel" },
                { "map.col.requirement", "Requirement" },
                { "map.col.payment", "Payment" },
                { "map.col.client", "Client" },
                // Location (action view) table
                { "loc.section.actions", "Action cards." },
                { "loc.section.clocks", "Clock cards." },
                { "loc.empty", "Nothing here." },
                { "loc.none", "none." },
                { "loc.takes-die", "a die" },
                { "loc.card-disabled", "Action card disabled." },
                { "loc.col.name", "Name" },
                { "loc.col.costs", "Costs" },
                { "loc.col.risk", "Risk" },
                { "loc.col.cost", "Per cycle" },
                { "loc.col.predicted", "Predicted" },
                { "loc.col.narrative", "Narrative" },
                { "loc.col.progress", "Progress" },
                // Dice allocation
                { "dice.picker", "Die picker." },
                { "dice.picker-closed", "Picker closed." },
                { "dice.refused", "Die unavailable." },
                { "dice.slotted", "Die slotted." },
                { "dice.slotted-item", "Slotted." },
                { "dice.returned", "Die returned." },
                { "dice.die", "Die" },
                { "dice.die-lower", "die" },
                { "dice.spent", "spent" },
                { "dice.broken", "broken" },
                { "dice.glitched", "glitched" },
                { "dice.health", "Health" },
                { "dice.health-lower", "health" },
                { "vitals.damaging", "Damaging rolls:" },
                { "crew.locked-in", "Locked in, crew slot" },
                { "crew.loadout", "Crew:" },
                { "crew.none", "No crew." },
                { "prompt.push", "P" },
                // Push voice (owner ruling batch 2026-08-02, D20)
                { "push.fired", "Push." },
                { "push.disarmed", "Push disarmed." },
                { "push.used", "Push used this cycle." },
                { "push.stress-full", "Push blocked: stress full." },
                { "push.glitch-full", "Push blocked: glitch full." },
                { "push.hub", "Push unavailable at a hub." },
                { "push.unavailable", "Push not available here." },
                { "push.confirm-bare", "Push armed. Press again to confirm." },
                { "push.rerolled", "rerolled" },
                { "push.boosted", "boosted" },
                { "push.focused", "focused" },
                { "push.glitched-noboost", "glitched, no boost." },
                { "push.factor-six", "reroll six" },
                { "push.factor-focus", "focus" },
                { "dice.focused", "focused" },
                { "vitals.contract-stress", "Contract stress" },
                { "skills.none", "no skill" },
                // Top-bar table
                { "topbar.title", "Top bar." },
                { "topbar.closed", "Closed." },
                { "topbar.empty", "Top bar not available." },
                { "topbar.button-suffix", " button." },
                { "topbar.row.vitals", "Vitals." },
                { "topbar.row.buttons", "Buttons." },
                { "topbar.row.dice", "Dice." },
                { "topbar.row-empty", "Nothing here." },
                // Action resolution
                { "outcome.positive", "positive outcome." },
                { "outcome.neutral", "neutral outcome." },
                { "outcome.negative", "negative outcome." },
                { "glyph.plus", "plus" },
                { "glyph.minus", "minus" },
                { "outcome.now", "now" },
                // Cycle summary
                { "cycle.prefix", "Cycle" },
                { "cycle.dice", "Dice:" },
                { "dice.crew", "Crew" },
                // Modes (F1)
                { "mode.title", "Title screen" },
                { "mode.pause", "Pause menu" },
                { "mode.tutorial", "Tutorial" },
                { "mode.conversation", "Conversation" },
                { "mode.diceallocation", "Die picker" },
                { "mode.map", "Map" },
                { "mode.character", "Character sheet" },
                // Character table
                { "char.empty", "No skill rows." },
                { "char.section.skills", "Skills." },
                { "char.section.push", "Push upgrades." },
                { "char.col.next", "Next" },
                { "char.rank", "rank" },
                { "char.costs", "costs" },
                { "char.points", "points" },
                { "char.complete", "Ladder complete." },
                { "char.blocked", "blocked" },
                { "char.owned", "Owned." },
                { "char.active", "Active." },
                { "char.tier", "Tier" },
                { "char.unavailable", "Upgrade not available." },
                { "char.not-enough", "Not enough points." },
                { "char.no-change", "No change." },
                { "char.confirm", "Confirm window. Enter confirms, Backspace cancels." },
                { "help.character", "Up and Down walk skills and push upgrades, Left and Right browse cells, Space full detail with points, Enter buys or switches, Backspace closes." },
                { "mode.cycletransition", "Cycle transition" },
                { "mode.actionview", "Location" },
                { "mode.travel", "Traveling" },
                { "mode.rigrooms", "Rig" },
                { "mode.station", "Station" },
                // F1 key summaries
                { "help.table", "Arrows walk the table, Left and Right browse facets, Space full row, Enter activates, Backspace goes back, V top bar, M map, G rig, U character sheet, I inventory, P push." },
                { "map.unavailable", "Map not available here." },
                { "rig.unavailable", "Rig toggle not available here." },
                { "help.topbar", "Up and Down walk the bar, Enter presses buttons, V or Backspace closes." },
                { "help.tutorial", "Up and Down walk the text, Enter continues." },
                { "help.conversation", "Numbers pick choices, Up and Down move — Up past the top walks back through the conversation, Down returns to the latest — Enter confirms." },
                { "help.dice", "Arrows move between dice, Enter picks, Backspace takes back or closes." },
                { "help.map", "Arrows walk the markers, Left and Right browse facets, Space full row, Enter commits travel, M or Backspace closes the map." },
                { "help.native", "Arrows and Enter, game controls." },
                // Options review
                { "options.intro", "Options. Up and Down for settings, Left and Right to change." },
                { "options.setting", "setting" },
                { "options.changed", "changed" },
                { "help.options", "Up and Down walk settings, Left and Right change them, Enter presses Back, Escape backs out." },
                // Guide reader
                { "guide.intro", "Guide. Up and Down for topics, Enter opens one." },
                { "guide.empty", "No topics." },
                { "guide.page-empty", "Page empty." },
                { "help.guide", "Up and Down walk topics, Enter opens; in the text, Up and Down move by paragraph, Left returns to topics, Space reads the whole page." },
                { "help.journal", "Up and Down walk drives, Left and Right browse cells, Enter performs the cell, Slash swaps tabs, J or Backspace closes." },
                // Inventory strip
                { "mode.inventory", "Inventory" },
                { "inv.title", "Inventory." },
                { "inv.closed", "Inventory closed." },
                { "inv.empty", "No items." },
                { "help.inventory", "Left and Right walk items, Space or Enter reads the description, M map, G rig, U character sheet, J drives, I or Backspace closes." },
                // Drive journal
                { "mode.drivelog", "Drive log" },
                { "journal.col.name", "Name" },
                { "journal.col.objectives", "Objectives" },
                { "journal.col.track", "Track" },
                { "journal.col.abandon", "Abandon" },
                { "journal.tracked", "tracked." },
                { "journal.tracked-cell", "Tracked." },
                { "journal.not-tracked", "Not tracked." },
                { "journal.tracking-prefix", "Tracking: " },
                { "journal.tracking-none", "Tracking nothing." },
                { "journal.abandon", "Abandon." },
                { "journal.no-abandon", "Cannot abandon." },
                { "journal.abandon-confirm", "Abandoning rolls back to the travel checkpoint. Press Enter again to confirm." },
                { "journal.no-objectives", "No objectives." },
                { "journal.empty", "No drives here." },
                { "journal.done", ", done" },
                { "journal.failed", ", failed" },
                { "journal.track-unavailable", "Track not available." },
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
