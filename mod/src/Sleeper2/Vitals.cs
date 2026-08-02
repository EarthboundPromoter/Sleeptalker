using System;
using System.Collections.Generic;
using UnityEngine;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Player vitals — the change-clock channel registry for the HUD bars
    /// (owner direction 2026-07-26; full channel set from decode D3, 2026-07-31 —
    /// docs/decodes/D3-vitals-clocks.md).
    ///
    /// The CS2 HUD renders each vital as a bar owned by an FSM that polls the value
    /// and enters an update state only when the bar actually changes. That state
    /// entry is a render-honest change clock, and the bar FSM's own variables are
    /// the value the bar draws. Each channel names that dial: a match predicate over
    /// the live FSM, the clock state(s), value/capacity readers. Announce-on-change
    /// rides the clock; the readout key and the top-bar table iterate Read().
    ///
    /// First signal after load caches silently (load catch-up is boot noise, same
    /// policy as the title-flow boot sweep); every later change announces.
    ///
    /// Channels are corpus-derived (D3, action payloads decoded) and PENDING LIVE
    /// VALIDATION; unknowns fail loudly (missing-variable and unknown-variant logs)
    /// per the offline-first amendment in docs/build-plan.md.</summary>
    internal static class Vitals
    {
        internal sealed class Channel
        {
            public string Name;                        // spoken name ("Stress"); null = SpokenName only
            public string OwnerPrefix;                 // owner GameObject name prefix ("" = any)
            public Func<PlayMakerFSM, bool> Match;     // extra predicate beyond the prefix (null = none)
            public string[] UpdateStates;              // the bar's change-clock state(s)
            public Func<PlayMakerFSM, float> Value;    // current value, from the bar's own vars
            public Func<PlayMakerFSM, float> Max;      // capacity; 0 = unknown, spoken without "of"
            public Func<PlayMakerFSM, string> SpokenName; // per-instance name (crew); null = Name
            public float? LastSpoken;                  // mod-side cache: direction + first-signal mute
            public PlayMakerFSM LiveFsm;               // last bar seen alive, for on-demand Read()
        }

        private static readonly List<Channel> Channels = new List<Channel>();

        public static void Init()
        {
            // Player stress. Bar family "Stress System {N} {Safe|Risky|Danger}"; the
            // difficulty selector activates exactly one variant, so signals self-select.
            // No band words render anywhere in the family (D3c) — value wording only.
            Register(new Channel
            {
                Name = Lex.T("vitals.stress"),
                OwnerPrefix = "Stress System ",
                UpdateStates = new[] { "Animation and Sound" },
                Value = fsm => FloatVar(fsm, "Stress"),
                Max = fsm => FirstNumberIn(fsm.gameObject.name),
            });

            // Energy. Clock = the "Energy Setter 0..6" family (exact CS1 parity, D3a) —
            // the seven setters ARE the render change; each quantizes the bar to a fixed
            // fill, so the bar renders as 5 boxes of 20 (D3b). Spoken value = box count
            // via the game's own Energy Checker FloatSwitch bands, reproduced here with
            // provenance (the LuaStore.SkillModifier precedent): <1→0, <21→1, <41→2,
            // <61→3, <81→4, else 5. Raw 0–100 becomes the detail form in the top-bar table.
            Register(new Channel
            {
                Name = Lex.T("vitals.energy"),
                OwnerPrefix = "Energy Bar System",
                UpdateStates = new[]
                {
                    "Energy Setter 0", "Energy Setter 1", "Energy Setter 2", "Energy Setter 3",
                    "Energy Setter 4", "Energy Setter 5", "Energy Setter 6",
                },
                Value = fsm => EnergyBoxes(FloatVar(fsm, "Player Energy")),
                Max = fsm => 5f,
            });

            // Crew stress. Same bar idiom per crew member; variant word carries the
            // capacity (Regular 6 / Resistant 8 / Weak 4 — D3, no digit in the owner
            // name). Damage and heal have separate clock states — register both.
            // PROVISIONAL WORDING: identity from the FSM's own Crew Identifier var
            // ("Crew1" → "Crew 1"); the rendered crew name lookup arrives with the
            // crew surfaces (Checkpoint C).
            Register(new Channel
            {
                OwnerPrefix = "Crew ",
                Match = fsm => fsm.gameObject.name.Contains("Stress"),
                UpdateStates = new[] { "Animation and Sound", "Animation and Sound 2" },
                Value = fsm => FloatVar(fsm, "Stress"),
                Max = CrewMax,
                SpokenName = fsm => CrewName(fsm) + " " + Lex.T("vitals.stress"),
            });

            // Fuel / Supplies / Cryo slots. All three Amount FSMs' owners are named
            // "Amount" (D3) — disambiguate by the parent slot object. "Cryo Slot " has a
            // trailing space in shipped data (wart register) — TrimEnd covers all three.
            // Capacity reads live from the sibling Capacity FSM's "* Limit" var; cryo has none.
            RegisterSlot("vitals.fuel", "Fuel Slot");
            RegisterSlot("vitals.supplies", "Supplies Slot");
            RegisterSlot("vitals.cryo", "Cryo Slot");

            // Glitch dials. UICircle renders /6; decrease clock "Set Bar" is silent
            // in-game, increase "Set Bar 2" adds sound — both are render changes, both
            // announce (D3).
            Register(new Channel
            {
                Name = Lex.T("vitals.glitch"),
                OwnerPrefix = "Glitch Markers",
                UpdateStates = new[] { "Set Bar", "Set Bar 2" },
                Value = fsm => FloatVar(fsm, "Health"),
                Max = fsm => 6f,
            });
            Register(new Channel
            {
                Name = Lex.T("vitals.permaglitch"),
                OwnerPrefix = "Permanent Glitch Clock",
                UpdateStates = new[] { "Set Bar", "Set Bar 2" },
                Value = fsm => FloatVar(fsm, "Health"),
                Max = fsm => 6f,
            });

            // NOT registered (deliberate): the contract-mode "Supplied" child renders a
            // supplies count in the energy area while the Supplies Slot channel may cover
            // the same change — validate on the first contract ride before wiring it
            // (double-speak risk). Edge states (Zero Energy, Supplies Empty,
            // "Srtress Overflow" [sic]) stay unspoken: the box/stress channels already
            // announce their consequences; the F3 ring captures the states themselves.
        }

        // Lane classification (owner law — render first, backing second):
        // slot counts RENDER AS TEXT on the Amount element itself → the text is
        // the value; the Value variable is the logged backing. Stress/energy/
        // crew/glitch bars render as FILLS with no number → their driving FSM
        // variables are the sanctioned practical lane (documented here).
        private static void RegisterSlot(string lexKey, string slotName)
        {
            Register(new Channel
            {
                Name = Lex.T(lexKey),
                OwnerPrefix = "Amount",
                Match = fsm => fsm.transform.parent != null
                    && fsm.transform.parent.name.TrimEnd() == slotName,
                UpdateStates = new[] { "Updating" },
                Value = fsm => RenderedNumber(fsm) ?? FloatVar(fsm, "Value"),
                Max = SiblingLimit,
            });
        }

        /// <summary>The number the slot actually draws — its own TMP text.</summary>
        private static float? RenderedNumber(PlayMakerFSM fsm)
        {
            var tmp = fsm.GetComponent<TMPro.TMP_Text>();
            if (tmp == null) return null;
            string text = SpeechService.Clean(tmp.text);
            if (string.IsNullOrEmpty(text)) return null;
            float n = Util.LeadingInt(text);
            if (n <= 0f && text.Trim() != "0")
            {
                LogOnce("[Vitals] slot text \"" + text + "\" on "
                    + fsm.transform.parent.name + " parses to no number — Value var backing");
                return null;
            }
            return n;
        }

        private static void Register(Channel ch)
        {
            Channels.Add(ch);
            foreach (var state in ch.UpdateStates)
                FsmSignals.Subscribe(null, state, (fsm, s) => OnChanged(ch, fsm));
        }

        private static void OnChanged(Channel ch, PlayMakerFSM fsm)
        {
            if (fsm == null || !fsm.gameObject.name.StartsWith(ch.OwnerPrefix)) return;
            if (ch.Match != null && !ch.Match(fsm)) return;
            ch.LiveFsm = fsm;

            float value = ch.Value(fsm);
            if (ch.LastSpoken == null)
            {
                ch.LastSpoken = value;   // load-time catch-up: cache, stay silent
                return;
            }
            if (value == ch.LastSpoken.Value) return;

            // Two-lane rule (CS1 ResourceWatch carried): while a resolution or a
            // cycle transition is in flight, the interaction lane speaks (outcome
            // deltas / the wake summary's absolutes) — this base lane caches
            // silently instead of double-speaking.
            if (ActionOutcomes.ResolutionInFlight || CycleGate.TransitionInFlight)
            {
                // CS1 ResourceWatch shape (owner direction, ride V5): the
                // observed change is HANDED to the outcome lane, never
                // discarded — amount + new total compose into the utterance.
                float before = ch.LastSpoken.Value;
                if (ActionOutcomes.ResolutionInFlight)
                    ActionOutcomes.OfferDelta(NameOf(ch, fsm),
                        value - before, Format(value, ch.Max(fsm)));
                ch.LastSpoken = value;
                Plugin.Log.LogInfo("[Vitals] stood down (interaction lane): "
                    + NameOf(ch, fsm) + " -> " + value);
                return;
            }

            string direction = Lex.T(value > ch.LastSpoken.Value ? "vitals.up" : "vitals.down");
            ch.LastSpoken = value;
            SpeechService.Say(NameOf(ch, fsm) + " " + direction + ", "
                + Format(value, ch.Max(fsm)) + ".", Priority.Queued, "vitals");
        }

        /// <summary>The current bar state for a rendered effect word ("ENERGY",
        /// "13 CRYO" — the card's own vocabulary): the matching channel's live
        /// "x of y". Null when no channel matches or its bar is gone. Feeds the
        /// outcome composition (owner ruling: gain/loss, then the resulting
        /// state).</summary>
        public static string CurrentFor(string renderedWord)
        {
            if (string.IsNullOrEmpty(renderedWord)) return null;
            string upper = renderedWord.ToUpperInvariant();
            foreach (var ch in Channels)
            {
                if (ch.Name == null) continue; // per-instance channels (crew) opt out
                if (!upper.Contains(ch.Name.ToUpperInvariant())) continue;
                var fsm = ch.LiveFsm;
                if (fsm == null || !fsm.gameObject.activeInHierarchy) continue;
                return Format(ch.Value(fsm), ch.Max(fsm));
            }
            return null;
        }

        /// <summary>Current readout of every channel with a live bar — the vitals
        /// readout and the top-bar table speak exactly this list.</summary>
        public static List<string> Read()
        {
            var lines = new List<string>();
            foreach (var ch in Channels)
            {
                var fsm = ch.LiveFsm;
                if (fsm == null || !fsm.gameObject.activeInHierarchy) continue;
                lines.Add(NameOf(ch, fsm) + " " + Format(ch.Value(fsm), ch.Max(fsm)) + ".");
            }
            return lines;
        }

        private static string NameOf(Channel ch, PlayMakerFSM fsm)
            => ch.SpokenName != null ? ch.SpokenName(fsm) : ch.Name;

        private static string Format(float value, float max)
        {
            string v = value.ToString("0.#");
            return max > 0f ? v + " " + Lex.T("vitals.of") + " " + max.ToString("0.#") : v;
        }

        /// <summary>The game's own Energy Checker FloatSwitch bands (D3a: lessThan
        /// 1/21/41/61/81 → setters 0..5), reproduced for the box count the setters
        /// render. Provenance: level2 pid 28353, decoded 2026-07-31.</summary>
        private static float EnergyBoxes(float energy)
        {
            if (energy < 1f) return 0f;
            if (energy < 21f) return 1f;
            if (energy < 41f) return 2f;
            if (energy < 61f) return 3f;
            if (energy < 81f) return 4f;
            return 5f;
        }

        private static float CrewMax(PlayMakerFSM fsm)
        {
            string name = fsm.gameObject.name;
            if (name.Contains("Regular")) return 6f;
            if (name.Contains("Resistant")) return 8f;
            if (name.Contains("Weak")) return 4f;
            LogOnce("[Vitals] UNKNOWN CREW VARIANT (no capacity): " + name);
            return 0f;
        }

        /// <summary>PROVISIONAL: crew identity from the bar's own Crew Identifier var
        /// ("Crew1" → "Crew 1"); replaced by the rendered crew name with the crew surfaces.</summary>
        private static string CrewName(PlayMakerFSM fsm)
        {
            var id = fsm.FsmVariables.GetFsmString("Crew Identifier");
            string raw = id != null && !string.IsNullOrEmpty(id.Value) ? id.Value : fsm.gameObject.name;
            for (int i = 1; i < raw.Length; i++)
                if (char.IsDigit(raw[i]) && !char.IsDigit(raw[i - 1]))
                    return raw.Substring(0, i) + " " + raw.Substring(i);
            return raw;
        }

        /// <summary>Capacity from the slot's sibling Capacity element. Render truth
        /// first (ride V1 finding: the FSM's limit var read 0 at announce time while
        /// the slot rendered "/5" — the Capacity object's own TMP text IS the
        /// rendered capacity), FSM "* Limit" var as fallback (float, then int).
        /// 0 when absent (cryo has no Capacity sibling).</summary>
        private static float SiblingLimit(PlayMakerFSM fsm)
        {
            var parent = fsm.transform.parent;
            if (parent == null) return 0f;
            var cap = parent.Find("Capacity");
            if (cap == null) return 0f;
            var tmp = cap.GetComponent<TMPro.TMP_Text>();
            if (tmp != null)
            {
                float rendered = Util.LeadingInt(SpeechService.Clean(tmp.text));
                if (rendered > 0f) return rendered;
            }
            var capFsm = cap.GetComponent<PlayMakerFSM>();
            if (capFsm == null) return 0f;
            foreach (var f in capFsm.FsmVariables.FloatVariables)
                if (f.Name.EndsWith("Limit") && f.Value > 0f) return f.Value;
            foreach (var i in capFsm.FsmVariables.IntVariables)
                if (i.Name.EndsWith("Limit") && i.Value > 0) return i.Value;
            LogOnce("[Vitals] Capacity under " + parent.name
                + " renders no number and its FSM has no positive * Limit var");
            return 0f;
        }

        private static float FloatVar(PlayMakerFSM fsm, string name)
        {
            var f = fsm.FsmVariables.GetFsmFloat(name);
            if (f == null)
            {
                LogOnce("[Vitals] MISSING VAR \"" + name + "\" on " + fsm.gameObject.name);
                return 0f;
            }
            return f.Value;
        }

        private static float FirstNumberIn(string name)
        {
            int start = -1;
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsDigit(name[i])) { if (start < 0) start = i; }
                else if (start >= 0) return float.Parse(name.Substring(start, i - start));
            }
            return start >= 0 ? float.Parse(name.Substring(start)) : 0f;
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void LogOnce(string line)
        {
            if (Logged.Add(line)) Plugin.Log.LogWarning(line);
        }
    }
}
