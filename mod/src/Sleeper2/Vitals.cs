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
            public bool StripRendered;                 // renders in the BOTTOM strip, not the top bar (D6)
            public bool AnnounceWhenDrawnOnly;         // suppress changes while the bar isn't drawn (crew, D10)
            public bool AnnounceOnly;                  // change clock only — never a Read()/readout line
            public bool TopBarRowExcluded;             // out of the V vitals row; wake summary keeps it (crew stress)
            public Func<string> Suffix;                // extra composed fact (stress: the damaging-rolls forecast)
            // Per-element caches, keyed by the spoken name (owner ruling
            // 2026-08-02, the die-health post-mortem: ONE shared cache across
            // five Dice Health bars spoke a false "Die 5 health down" and
            // swallowed two real restores — every individual element tracks
            // individually). Name-keyed, not instance-keyed, so the contract
            // stress channel's same-titled bar COPIES still dedupe (their
            // storm rides one key) while distinct elements never collide.
            public readonly Dictionary<string, float> LastByName
                = new Dictionary<string, float>();     // direction + first-signal mute, per element
            public readonly Dictionary<string, PlayMakerFSM> LiveByName
                = new Dictionary<string, PlayMakerFSM>(); // live bar per element, for on-demand Read()
        }

        private static readonly List<Channel> Channels = new List<Channel>();
        private static Channel _contractChannel;

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
                // Owner build 2026-08-02 (guide-confirmed): the white markers
                // above the stress bar forecast which ROLLS damage dice —
                // composed from the same variables the game's own Stress
                // Check N compares (the blocked-travel precedent).
                Suffix = DamagingRolls,
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
                SpokenName = fsm => CrewSpokenName(fsm) + " " + Lex.T("vitals.stress"),
                // Owner ruling 2026-08-02: crew bars poll while UNDRAWN outside
                // contracts — those changes cache silently (wake summary +
                // contract panels carry the truth). Escape hatch: this flag.
                AnnounceWhenDrawnOnly = true,
                // Owner ruling 2026-08-02 (second pass): crew stress reviews in
                // the member's OWN V-table row, not the vitals row; change
                // announcements and the wake summary keep their lines.
                TopBarRowExcluded = true,
            });

            // Fuel / Supplies / Cryo slots. All three Amount FSMs' owners are named
            // "Amount" (D3) — disambiguate by the parent slot object. "Cryo Slot " has a
            // trailing space in shipped data (wart register) — TrimEnd covers all three.
            // Capacity reads live from the sibling Capacity FSM's "* Limit" var; cryo has none.
            RegisterSlot("vitals.fuel", "Fuel Slot");
            RegisterSlot("vitals.supplies", "Supplies Slot");
            RegisterSlot("vitals.cryo", "Cryo Slot");

            // Contract stress (owner report 2026-08-02; tutorial text of
            // record: "Contracts also have their own CONTRACT STRESS. When
            // this bar is filled the contract will FAIL."). The bar family is
            // the exact player/crew stress graph — one standing copy on the
            // Contract Stress Display plus one per action group (census
            // level4+), all reading the same per-contract key; the name-keyed
            // cache dedups the copy storm (all copies render the same title =
            // one key). MATCH is mechanism-keyed (capture audit 2026-08-03:
            // bar owners are named per contract — Derelict, Drone, Suit… —
            // and the old "Derelict" prefix missed every non-Derelict
            // contract): a contract bar is any stress bar carrying the
            // INIT-built "Contract Stress Variable" key var. Registration
            // gives the change announcements, the V-row readout, AND the
            // two-lane handoff into resolution reads. Cap: the bar FSM's own
            // clamp constant (decode 2026-08-03: Derelict 12, Drone 4 —
            // per-contract; the assumed crew-idiom 6 was wrong everywhere).
            _contractChannel = new Channel
            {
                Name = Lex.T("vitals.contract-stress"),
                OwnerPrefix = "",
                // Find*, never Get* for existence (post-mortem 2026-08-03:
                // GetFsmXxx AUTO-CREATES missing variables — a Get*-based
                // existence check is always true, which made this matcher
                // claim every stress bar incl. the player's).
                Match = fsm => fsm.gameObject.name.Contains("Stress")
                    && fsm.FsmVariables.FindFsmString("Contract Stress Variable") != null,
                UpdateStates = new[] { "Animation and Sound" },
                Value = fsm => FloatVar(fsm, "Stress"),
                Max = ContractCap,
                // Owner ruling 2026-08-02: the bar's own rendered TITLE names
                // the contract and changes per job — speak it (Name stays the
                // generic for effect-word matching).
                SpokenName = ContractStressName,
                // Owner design 2026-08-03: every contract-stress read carries
                // the future crisis points — "x of y. Crises at 9." — with
                // already-spawned crises excluded.
                Suffix = CrisisForecast,
            };
            Register(_contractChannel);

            // Dice health (owner build 2026-08-02; guide-confirmed: three
            // health per die, break at zero, repair costs glitch). The
            // per-slot Dice Health bar is the render — "Set Bar" is its
            // change clock, Color Change → Red the low warning; backing =
            // the Die<N>_Health key family the game's own Dice Damage writes.
            Register(new Channel
            {
                OwnerPrefix = "Dice Health",
                UpdateStates = new[] { "Set Bar" },
                Value = fsm => FloatVar(fsm, "Health"),
                Max = fsm => 3f,
                SpokenName = DieHealthName,
                // Change clock ONLY (ride bug 2026-08-02: a "Die health 3 of
                // 3" cell landed in the V vitals row) — standing health rides
                // the DICE row's own die reads, not the vitals readout.
                AnnounceOnly = true,
            });

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

            // Crew task payout states (decode 2026-08-03: the Cycle
            // Controller's per-tier Cryo/Fuel/Supplies states are the ONLY
            // moment the payout exists — hub-scene cycles only). The
            // OnCrewPayout guard narrows the generic names to the controller.
            foreach (var state in new[]
                { "Cryo", "Fuel", "Supplies", "Cryo 2", "Fuel 2", "Supplies 2",
                  "Cryo 3", "Fuel 3", "Supplies 3" })
                FsmSignals.Subscribe(null, state, OnCrewPayout);

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
                // D6: these slots ARE the bottom inventory strip — the top bar
                // never renders them (owner ruling 2026-08-02: the V table cuts
                // them; the InventoryTable is their review surface).
                StripRendered = true,
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

        /// <summary>The value a bar's element may SPEAK: floored at zero and
        /// capped at its capacity (owner ruling 2026-08-02, the "-1 of 10" /
        /// "7 of 6" post-mortem: the game's backing vars overflow in BOTH
        /// directions during scripted sequences and disable triggers —
        /// the screen never draws past the bounds, so neither do we). The
        /// raw value logs loudly whenever the clamp bites.</summary>
        private static float Bounded(Channel ch, PlayMakerFSM fsm, string key, out float max)
        {
            max = ch.Max(fsm);
            float raw = ch.Value(fsm);
            float value = raw < 0f ? 0f : (max > 0f && raw > max ? max : raw);
            if (value != raw)
                LogOnce("[Vitals] CLAMPED " + key + ": raw " + raw
                    + " spoken as " + value + " (bounds 0.."
                    + (max > 0f ? max.ToString("0.#") : "open") + ")");
            return value;
        }

        private static void OnChanged(Channel ch, PlayMakerFSM fsm)
        {
            if (fsm == null || !fsm.gameObject.name.StartsWith(ch.OwnerPrefix)) return;
            if (ch.Match != null && !ch.Match(fsm)) return;
            string key = NameOf(ch, fsm);
            ch.LiveByName[key] = fsm;

            float value = Bounded(ch, fsm, key, out float _);
            if (!ch.LastByName.TryGetValue(key, out float last))
            {
                ch.LastByName[key] = value;   // load-time catch-up: cache, stay silent
                return;
            }
            if (value == last)
            {
                // A clamped-region write renders as NO change — but the push
                // cost latch still needs the signal (sync pass MED-3: deep-
                // negative raw stress + cost clamped 0→0 wedged the compose
                // at its deadline).
                if (PushFlow.FireInFlight) PushFlow.NoteNullDelta(key);
                return;
            }

            if (HandOffIfLaneInFlight(ch, key, fsm, value, last)) return;

            // Ambient announces go through a short settle-verify (owner
            // ruling 2026-08-02, the glitch 3-then-2 misreport: scripted
            // sequences mutate vitals in bursts, and an intermediate value
            // spoken in flight is misinformation). The spoken value re-reads
            // at speech time; a burst extends the wait and collapses to its
            // settled value; a round-trip back to the cached value goes
            // silent. Aged out loudly so a never-quiet bar cannot starve.
            EnqueueVerify(ch, key, fsm);
        }

        /// <summary>The lane handoffs (push, then the two-lane rule) — checked
        /// both at signal time and again at verified-speech time. True = the
        /// change was consumed (cache updated).</summary>
        private static bool HandOffIfLaneInFlight(
            Channel ch, string key, PlayMakerFSM fsm, float value, float last)
        {
            // Push lane first (sync pass MED-9: before the undrawn gate so a
            // RALLY crew heal on a momentarily undrawn bar cannot vanish).
            if (PushFlow.FireInFlight)
            {
                PushFlow.OfferDelta(key, value - last, Format(value, ch.Max(fsm)));
                ch.LastByName[key] = value;
                Plugin.Log.LogInfo("[Vitals] stood down (push lane): "
                    + key + " -> " + value);
                return true;
            }

            // Undrawn-bar suppression (owner ruling 2026-08-02, crew channel):
            // a change on a bar the screen isn't drawing caches silently.
            if (ch.AnnounceWhenDrawnOnly && !Util.RenderedUp(fsm.transform))
            {
                ch.LastByName[key] = value;
                return true;
            }

            // Two-lane rule (CS1 ResourceWatch carried): while a resolution or
            // a cycle transition is in flight, the interaction lane speaks —
            // the observed change is HANDED over, never discarded. Hand-off
            // requires an OPEN capture (sync pass HIGH-2: the post-read TAIL
            // has no consumer — a tail delta speaks ambient instead of being
            // cleared unspoken at the next capture).
            bool capturePending = ActionOutcomes.CapturePending;
            if (capturePending || CycleGate.TransitionInFlight)
            {
                if (capturePending)
                    ActionOutcomes.OfferDelta(key, ch.Name ?? key,
                        value - last, Format(value, ch.Max(fsm)));
                // Cycle-lane contract stress and resource slots keep their
                // deltas for the wake summary (owner design 2026-08-03)
                // instead of vanishing into the silent cache.
                else if ((ch == _contractChannel || ch.StripRendered)
                    && value != last)
                    NoteCycleDelta(key, value - last);
                ch.LastByName[key] = value;
                Plugin.Log.LogInfo("[Vitals] stood down (interaction lane): "
                    + key + " -> " + value);
                return true;
            }
            return false;
        }

        private sealed class PendingAnnounce
        {
            public Channel Ch;
            public string Key;
            public PlayMakerFSM Fsm;
            public int Quiet;   // ticks since the last change signal
            public int Age;     // total ticks pending
        }

        private static readonly List<PendingAnnounce> _pending =
            new List<PendingAnnounce>();
        private const int VerifyQuietTicks = 15;  // ~0.25s of no further writes
        private const int VerifyMaxAgeTicks = 90; // never-quiet backstop, logged

        private static void EnqueueVerify(Channel ch, string key, PlayMakerFSM fsm)
        {
            foreach (var p in _pending)
                if (p.Ch == ch && p.Key == key) { p.Quiet = 0; p.Fsm = fsm; return; }
            _pending.Add(new PendingAnnounce { Ch = ch, Key = key, Fsm = fsm });
        }

        public static void Tick()
        {
            PayoutTick();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                p.Quiet++;
                p.Age++;
                if (p.Quiet < VerifyQuietTicks && p.Age < VerifyMaxAgeTicks) continue;
                if (p.Age >= VerifyMaxAgeTicks && p.Quiet < VerifyQuietTicks)
                    Plugin.Log.LogWarning("[Vitals] " + p.Key
                        + " never went quiet — aged announce (capture)");
                _pending.RemoveAt(i);
                SpeakVerified(p);
            }
        }

        private static void SpeakVerified(PendingAnnounce p)
        {
            var ch = p.Ch;
            var fsm = p.Fsm;
            if (fsm == null || fsm.gameObject == null
                || !fsm.gameObject.activeInHierarchy)
                return; // bar gone mid-verify: cache untouched, next signal re-diffs
            float value = Bounded(ch, fsm, p.Key, out float max);
            if (!ch.LastByName.TryGetValue(p.Key, out float last)
                || value == last)
            {
                ch.LastByName[p.Key] = value;  // round-tripped: silent
                return;
            }
            if (HandOffIfLaneInFlight(ch, p.Key, fsm, value, last)) return;
            string direction = Lex.T(value > last ? "vitals.up" : "vitals.down");
            ch.LastByName[p.Key] = value;
            string announce = p.Key + " " + direction + ", "
                + Format(value, max) + ".";
            string extra = ch.Suffix != null ? ch.Suffix() : null;
            if (extra != null) announce += " " + extra;
            SpeechService.Say(announce, Priority.Queued, "vitals");
        }

        /// <summary>The LONGEST channel name contained in a rendered word —
        /// the channel-identity match (sync pass MED-4: "CONTRACT STRESS"
        /// contains "Stress" and "PERMANENT GLITCH" contains "Glitch"; the
        /// longer, more specific channel owns the word). Null off-family.</summary>
        public static string CanonicalFor(string renderedWord)
        {
            if (string.IsNullOrEmpty(renderedWord)) return null;
            string upper = renderedWord.ToUpperInvariant();
            string best = null;
            foreach (var ch in Channels)
            {
                if (ch.Name == null) continue; // per-instance channels (crew) opt out
                if (!upper.Contains(ch.Name.ToUpperInvariant())) continue;
                if (best == null || ch.Name.Length > best.Length) best = ch.Name;
            }
            return best;
        }

        /// <summary>The current bar state for a rendered effect word ("ENERGY",
        /// "13 CRYO" — the card's own vocabulary): the matching channel's live
        /// "x of y", longest-name-wins. Null when no channel matches or its
        /// bar is gone. Feeds the outcome composition (owner ruling: gain/
        /// loss, then the resulting state).</summary>
        public static string CurrentFor(string renderedWord)
        {
            string best = CanonicalFor(renderedWord);
            if (best == null) return null;
            foreach (var ch in Channels)
            {
                if (ch.Name != best) continue;
                var fsm = FirstLive(ch);
                if (fsm == null) continue;
                float v = Bounded(ch, fsm, ch.Name, out float m);
                return Format(v, m);
            }
            return null;
        }

        /// <summary>Any live bar of the channel (single-element channels have
        /// exactly one; same-titled copies are interchangeable).</summary>
        private static PlayMakerFSM FirstLive(Channel ch)
        {
            foreach (var kv in ch.LiveByName)
            {
                var fsm = kv.Value;
                if (fsm != null && fsm.gameObject != null
                    && fsm.gameObject.activeInHierarchy) return fsm;
            }
            return null;
        }

        /// <summary>Current readout of every channel with a live bar — the vitals
        /// readout and the top-bar table speak exactly this list. The top-bar
        /// table passes includeStrip=false (owner ruling 2026-08-02): fuel/
        /// supplies/cryo render in the BOTTOM strip, so the V table surfaces
        /// only what the top bar draws; the wake summary keeps the full set
        /// (absolute-state composition, not a bar mirror).</summary>
        public static List<string> Read(bool includeStrip = true,
            bool cycleSummary = false)
        {
            var lines = new List<string>();
            foreach (var ch in Channels)
            {
                if (ch.AnnounceOnly) continue;
                if (!includeStrip && (ch.StripRendered || ch.TopBarRowExcluded)) continue;
                // Every live element speaks its own line (owner ruling
                // 2026-08-02: individual tracking — the shared-LiveFsm read
                // listed only the LAST-fired crew bar in the wake summary).
                foreach (var kv in ch.LiveByName)
                {
                    var fsm = kv.Value;
                    if (fsm == null || fsm.gameObject == null
                        || !fsm.gameObject.activeInHierarchy) continue;
                    // Drawn-only channels (crew) read only what the screen
                    // draws (owner ruling 2026-08-02: crew data never speaks
                    // outside a contract — an undrawn bar is garbage at best).
                    if (ch.AnnounceWhenDrawnOnly && !Util.RenderedUp(fsm.transform))
                        continue;
                    float v = Bounded(ch, fsm, kv.Key, out float m);
                    string line = ch == _contractChannel && cycleSummary
                        ? ContractCycleLine(kv.Key, v, m)
                        : ch.StripRendered && cycleSummary
                            ? SlotCycleLine(ch, kv.Key, v, m)
                            : kv.Key + " " + Format(v, m) + ".";
                    string suffix = ch.Suffix != null ? ch.Suffix() : null;
                    if (suffix != null) line += " " + suffix;
                    lines.Add(line);
                }
            }
            return lines;
        }

        // Cycle-window deltas (owner design 2026-08-03: contract stress and
        // crew task payouts get first-class resolution treatment — both land
        // INSIDE the cycle rollover, where the transition stand-down was
        // silently caching them away; the wake summary now speaks them in
        // the delta grammar, factored when a known cause owns them).
        private sealed class CycleDelta { public float Delta; public float At; }
        private static readonly Dictionary<string, CycleDelta> _cycleDeltas
            = new Dictionary<string, CycleDelta>();

        private static void NoteCycleDelta(string key, float delta)
        {
            if (_cycleDeltas.TryGetValue(key, out var d)
                && Time.unscaledTime - d.At < 60f)
                d.Delta += delta;
            else
                _cycleDeltas[key] = new CycleDelta { Delta = delta };
            _cycleDeltas[key].At = Time.unscaledTime;
        }

        // Crew task payouts (decode 2026-08-03, Cycle Controller: Crew
        // Actions → Crew Number tier (<3 / 3 / 4+) → Cryo|Fuel|Supplies
        // states — a RandomInt roll written straight to the store, NO render
        // and NO sound anywhere: the variables lane is the only source, per
        // the render-first law's escape clause, logged here). The roll is
        // read one tick after the state fires (actions may still be
        // running at signal time).
        private static readonly Dictionary<string, CycleDelta> _crewPayouts
            = new Dictionary<string, CycleDelta>();
        private static PlayMakerFSM _payoutFsm;
        private static string _payoutChannel;

        private static void OnCrewPayout(PlayMakerFSM fsm, string state)
        {
            // Mechanism key: the Cycle Controller carries the roll var, the
            // add accumulator AND the crew tally (generic state names like
            // "Fuel" exist elsewhere — the location dials pass through
            // "Cryo" at activation and fired this spuriously when the guard
            // used auto-creating Get* calls, post-mortem 2026-08-03).
            if (fsm == null
                || fsm.FsmVariables.FindFsmInt("Random Add") == null
                || fsm.FsmVariables.FindFsmFloat("To Add") == null
                || fsm.FsmVariables.FindFsmFloat("Crew Tally") == null) return;
            _payoutFsm = fsm;
            _payoutChannel = state.StartsWith("Cryo") ? Lex.T("vitals.cryo")
                : state.StartsWith("Fuel") ? Lex.T("vitals.fuel")
                : Lex.T("vitals.supplies");
        }

        private static void PayoutTick()
        {
            if (_payoutFsm == null) return;
            var fsm = _payoutFsm;
            _payoutFsm = null;
            if (fsm.gameObject == null) return;
            var roll = fsm.FsmVariables.FindFsmInt("Random Add");
            if (roll == null) return;
            Plugin.Log.LogInfo("[Vitals] crew task payout: " + _payoutChannel
                + " +" + roll.Value + " on " + fsm.gameObject.name
                + " (variables lane — the game renders nothing)");
            if (roll.Value <= 0) return; // the chance missed: no movement to attribute
            _crewPayouts[_payoutChannel]
                = new CycleDelta { Delta = roll.Value, At = Time.unscaledTime };
        }

        /// <summary>The wake summary's contract stress line: "TITLE up 2,
        /// crises, now 7 of 12." when the cycle moved it (factor tag only
        /// when the game's CRISIS count exactly owns the delta — anything
        /// else speaks plain and logs for capture); the standing absolute
        /// line otherwise. Consuming read — the V row goes back to
        /// absolute.</summary>
        private static string ContractCycleLine(string key, float v, float m)
        {
            if (!_cycleDeltas.TryGetValue(key, out var d))
                return key + " " + Format(v, m) + ".";
            _cycleDeltas.Remove(key);
            if (d.Delta == 0f || Time.unscaledTime - d.At > 60f)
                return key + " " + Format(v, m) + ".";
            float crises = LuaStore.NumF("CRISIS") ?? 0f;
            string factor = "";
            if (d.Delta > 0f && crises > 0f && Mathf.Abs(d.Delta - crises) < 0.01f)
                factor = ", " + Lex.T(crises >= 2f
                    ? "vitals.factor-crises" : "vitals.factor-crisis");
            else if (crises > 0f)
                LogOnce("[Vitals] cycle contract delta " + d.Delta + " with "
                    + crises + " crises active — unattributed (capture)");
            return key + " " + Lex.T(d.Delta > 0f ? "vitals.up" : "vitals.down")
                + " " + Mathf.Abs(d.Delta).ToString("0.#") + factor
                + ", " + Lex.T("outcome.now") + " " + Format(v, m) + ".";
        }

        /// <summary>The wake summary's resource slot line: "FUEL up 2, crew,
        /// now 4 of 10." when a crew task payout exactly owns the cycle's
        /// movement; a plain factored-less delta when a payout is in flight
        /// but doesn't match the movement (mixed causes — logged for
        /// capture); the standing absolute line for ordinary cycle movement
        /// (depletion) — the CS1 absolute-totals doctrine holds there.</summary>
        private static string SlotCycleLine(Channel ch, string key, float v, float m)
        {
            bool hasDelta = _cycleDeltas.TryGetValue(key, out var d)
                && Time.unscaledTime - d.At < 60f && d.Delta != 0f;
            _cycleDeltas.Remove(key);
            CycleDelta pay = null;
            if (ch.Name != null && _crewPayouts.TryGetValue(ch.Name, out pay))
                _crewPayouts.Remove(ch.Name);
            if (pay != null && Time.unscaledTime - pay.At > 60f) pay = null;
            if (!hasDelta || pay == null)
                return key + " " + Format(v, m) + ".";
            string factor = "";
            if (d.Delta > 0f && Mathf.Abs(d.Delta - pay.Delta) < 0.01f)
                factor = ", " + Lex.T("vitals.factor-crew");
            else
                LogOnce("[Vitals] cycle " + key + " delta " + d.Delta
                    + " vs crew payout " + pay.Delta + " — unattributed (capture)");
            return key + " " + Lex.T(d.Delta > 0f ? "vitals.up" : "vitals.down")
                + " " + Mathf.Abs(d.Delta).ToString("0.#") + factor
                + ", " + Lex.T("outcome.now") + " " + Format(v, m) + ".";
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

        /// <summary>"Die 3 health" — the slot index from the bar's own home
        /// ("Dice Slot N/Dice Health").</summary>
        private static string DieHealthName(PlayMakerFSM fsm)
        {
            for (var t = fsm.transform; t != null; t = t.parent)
                if (t.name.StartsWith("Dice Slot "))
                    return Lex.T("dice.die") + " " + Util.LeadingInt(t.name.Substring(10)).ToString("0")
                        + " " + Lex.T("dice.health-lower");
            return Lex.T("dice.die") + " " + Lex.T("dice.health-lower");
        }

        /// <summary>The contract stress bar's own rendered title — it names the
        /// contract and changes per job (owner ruling 2026-08-02: speak the
        /// true rendered name; "Contract stress" is the logged fallback and
        /// the effect-word matching handle). Searched two levels above the
        /// bar (its stress block, then the display group), first rendered
        /// non-numeric text wins.</summary>
        private static string ContractStressName(PlayMakerFSM fsm)
        {
            var t = fsm.transform.parent;
            for (int depth = 0; t != null && depth < 2; depth++, t = t.parent)
            {
                foreach (var tmp in t.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (tmp.transform == fsm.transform) continue;
                    if (!Util.RenderedUp(tmp.transform)) continue;
                    string text = SpeechService.Clean(tmp.text);
                    if (string.IsNullOrEmpty(text)) continue;
                    if (Util.LeadingInt(text) > 0 || text == "/" || text == "-") continue;
                    return text;
                }
            }
            LogOnce("[Vitals] contract stress bar renders no title — generic spoken");
            return Lex.T("vitals.contract-stress");
        }

        /// <summary>The rolls that damage dice RIGHT NOW — computed from the
        /// SAME variables the game's own Stress Check N compares (die FSM
        /// Stress Trigger 1..6 vs the stress the bar draws; the white markers
        /// above the stress bar are the render of this mapping — guide text
        /// of record 2026-08-02). Null when no face qualifies. Triggers are
        /// runtime-written (data defaults are 0): all-zero reads as unwritten
        /// and stays silent, logged once for capture — a trigger of 0 meaning
        /// "always damages" would hide here; the first ride decides.</summary>
        public static string DamagingRolls()
        {
            PlayMakerFSM bar = null;
            foreach (var ch in Channels)
                if (ch.OwnerPrefix == "Stress System ") { bar = FirstLive(ch); break; }
            if (bar == null) return null;
            float stress = FloatVar(bar, "Stress");

            var die = GameObject.Find("Letterbox Canvas/Top UI/Dice UI/Dice Slot 1/Die");
            var dieFsm = die != null ? die.GetComponent<PlayMakerFSM>() : null;
            if (dieFsm == null) return null;
            var faces = new List<int>();
            bool anyWritten = false;
            for (int n = 1; n <= 6; n++)
            {
                var trigger = dieFsm.FsmVariables.GetFsmFloat("Stress Trigger " + n);
                if (trigger == null || trigger.Value <= 0f) continue;
                anyWritten = true;
                if (stress >= trigger.Value) faces.Add(n);
            }
            if (!anyWritten)
            {
                if (stress > 0f)
                    LogOnce("[Vitals] stress triggers unwritten at stress " + stress
                        + " — damage forecast silent (capture)");
                return null;
            }
            if (faces.Count == 0) return null;
            var parts = new string[faces.Count];
            for (int i = 0; i < faces.Count; i++) parts[i] = faces[i].ToString();
            return Lex.T("vitals.damaging") + " " + string.Join(", ", parts) + ".";
        }

        /// <summary>The contract stress cap, read from the bar's OWN math
        /// (decode 2026-08-03: the Get Stress state clamps to a per-contract
        /// constant and fills at value/constant — Derelict 12, Drone 4).
        /// FloatClamp's maxValue is the authority, FloatDivide's divideBy the
        /// fallback; neither found logs once and speaks the bare value.</summary>
        private static readonly Dictionary<PlayMakerFSM, float> _contractCaps
            = new Dictionary<PlayMakerFSM, float>();

        internal static float ContractCap(PlayMakerFSM fsm)
        {
            if (_contractCaps.TryGetValue(fsm, out float cached)) return cached;
            float cap = 0f;
            var states = fsm.Fsm != null ? fsm.Fsm.States : null;
            if (states != null)
                foreach (var state in states)
                {
                    if (state.Name != "Get Stress") continue;
                    foreach (var action in state.Actions)
                    {
                        if (action == null) continue;
                        string type = action.GetType().Name;
                        string field = type == "FloatClamp" ? "maxValue"
                            : type == "FloatDivide" ? "divideBy" : null;
                        if (field == null) continue;
                        var f = action.GetType().GetField(field);
                        var v = f != null
                            ? f.GetValue(action) as HutongGames.PlayMaker.FsmFloat
                            : null;
                        if (v != null && v.Value > 0f) { cap = v.Value; break; }
                    }
                    break;
                }
            if (cap <= 0f)
                LogOnce("[Vitals] contract stress cap not found on "
                    + fsm.gameObject.name + " — bare value spoken");
            _contractCaps[fsm] = cap;
            return cap;
        }

        /// <summary>Future crisis points along the contract stress track
        /// (owner design 2026-08-03: "x of y, then crises at a, b, c",
        /// spawned ones excluded). The spawn criteria live on the crisis
        /// locations' OWN dials — "Crisis Location?" true, a Trigger On
        /// variable naming this contract's stress key, its Lower Limit the
        /// threshold (the game's x.9 gate: Fuel Leak spawns at >= 8.9,
        /// spoken 9). "&lt;Identifier&gt;_Crisis_Added" >= 1 in the store =
        /// already spawned. Null with no live bar, no key, or nothing
        /// ahead.</summary>
        private static string CrisisForecast()
        {
            var bar = _contractChannel != null ? FirstLive(_contractChannel) : null;
            if (bar == null) return null;
            var keyVar = bar.FsmVariables.FindFsmString("Contract Stress Variable");
            string stressKey = keyVar != null ? keyVar.Value : null;
            if (string.IsNullOrEmpty(stressKey)) return null;

            var points = new List<int>();
            foreach (var container in StationAtlas.NodeContainers())
            {
                if (container == null) continue;
                foreach (Transform node in container)
                {
                    foreach (var fsm in node.GetComponents<PlayMakerFSM>())
                    {
                        var isCrisis = fsm.FsmVariables.FindFsmBool("Crisis Location?");
                        if (isCrisis == null || !isCrisis.Value) continue;
                        float threshold = CrisisThreshold(fsm, stressKey);
                        if (threshold <= 0f) continue;
                        var id = fsm.FsmVariables.FindFsmString("Location Identifier");
                        if (id != null && !string.IsNullOrEmpty(id.Value)
                            && (LuaStore.NumF(id.Value + "_Crisis_Added") ?? 0f) >= 1f)
                            continue;
                        int point = Mathf.CeilToInt(threshold);
                        if (!points.Contains(point)) points.Add(point);
                    }
                }
            }
            if (points.Count == 0) return null;
            points.Sort();
            var parts = new string[points.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = points[i].ToString();
            return Lex.T("vitals.crises") + " " + string.Join(", ", parts) + ".";
        }

        /// <summary>The dial's spawn threshold against THIS contract's stress
        /// key — either Trigger On slot may carry it; 0 = not stress-keyed
        /// (a crisis gated on some other variable stays unlisted).</summary>
        private static float CrisisThreshold(PlayMakerFSM fsm, string stressKey)
        {
            var v1 = fsm.FsmVariables.FindFsmString("Trigger On Variable");
            if (v1 != null && v1.Value == stressKey)
            {
                var l = fsm.FsmVariables.FindFsmFloat("Trigger On Lower Limit");
                if (l != null) return l.Value;
            }
            var v2 = fsm.FsmVariables.FindFsmString("Trigger On 2 Variable");
            if (v2 != null && v2.Value == stressKey)
            {
                var l = fsm.FsmVariables.FindFsmFloat("Trigger On 2 Lower Limit");
                if (l != null) return l.Value;
            }
            return 0f;
        }

        internal static float CrewMax(PlayMakerFSM fsm)
        {
            string name = fsm.gameObject.name;
            if (name.Contains("Regular")) return 6f;
            if (name.Contains("Resistant")) return 8f;
            if (name.Contains("Weak")) return 4f;
            LogOnce("[Vitals] UNKNOWN CREW VARIANT (no capacity): " + name);
            return 0f;
        }

        /// <summary>Crew identity for speech: the RENDERED panel name (owner
        /// ruling 2026-08-02 — the provisional wording graduated), falling to
        /// the old identifier-derived "Crew N" when the panel isn't drawn.</summary>
        private static string CrewSpokenName(PlayMakerFSM fsm)
        {
            int member = CrewPanel.MemberOfTransform(fsm.transform);
            if (member > 0)
            {
                string rendered = CrewPanel.NameOf(member);
                if (rendered != null) return rendered;
            }
            return CrewName(fsm);
        }

        /// <summary>Fallback crew identity from the bar's own Crew Identifier var
        /// ("Crew1" → "Crew 1").</summary>
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
