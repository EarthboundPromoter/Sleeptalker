using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Action resolution announcements (owner directions 2026-07-31; CS1
    /// ActionOutcomes re-derived): the tier speaks at the outcome signal, and the
    /// content read fires at the card's own settle MOMENTS, never on wall-clock
    /// (owner redesign 2026-08-01 — the delay report; supersedes both the
    /// rest-event read and the deadline-as-normal-path): the render moment =
    /// every toast row the capture would read is DRAWN (they fade in 0.17s,
    /// D14 — RenderedUp arrival is the event, watched per frame); the write
    /// moment = no toast rows exist at all (narrative + watched deltas + clocks
    /// need none). The 2s deadline survives ONLY as the fail-loud backstop.
    ///
    /// COMPOSITION RULES (owner: full-featured deltas, one composed utterance):
    ///   narrative (the card's fresh Description) →
    ///   rendered effect lines (leading +/- glyph runs anywhere visible on the
    ///   card, GlyphRun-transcoded: "minus 2 ENERGY", "plus 15 CRYO") →
    ///   clock movements ("name, x of y" — UICircle render reads).
    /// Composed as ONE line so the modal pen (latest-per-source) can't shred it.
    /// EVERYTHING reads render first; variables back it only when render fails,
    /// loudly (owner law).
    ///
    /// Two-lane rule (CS1 ResourceWatch): while a resolution is in flight the
    /// Vitals channels stand down — the outcome lane speaks the deltas the card
    /// renders; the bars' own clocks stay the base-truth lane otherwise.
    ///
    /// Controller class = the Action Identifier variable (all variants, D2);
    /// variant-swap survival by rendered-name re-find (CS1 idiom).</summary>
    internal static class ActionOutcomes
    {
        private sealed class Pending
        {
            public string Name;
            public string Row;     // the achieved tier's OUTCOMES row name —
                                   // the settle/effect scope (decode 2026-08-02:
                                   // the LOSING tiers' lines + PREDICTIVE sit
                                   // active-at-alpha-0 INSIDE OUTCOMES on every
                                   // three-tier card, so whole-container
                                   // drawn==active was unsatisfiable — all 31
                                   // contract resolutions rode the backstop)
            public Transform Card;
            public Transform GroupParent;
            public float Deadline; // fail-loud backstop ONLY, never the path
            // Frames this pending has been watched: the zero-toast (write-
            // moment) read waits for the second sighting so a state whose
            // toasts activate on its entry frame is never mistaken for
            // effect-less (D14: rows activate at outcome entry, alpha 0,
            // then fade in — an ACTIVE row parks the read until drawn).
            public int SeenTicks;
        }

        private static readonly Dictionary<PlayMakerFSM, Pending> Pendings =
            new Dictionary<PlayMakerFSM, Pending>();
        // The CS1 ResourceWatch handoff (owner direction, ride V5): deltas the
        // standing-down vitals lane observed during this resolution — the
        // PRIMARY state math; render effect lines cover only what no watched
        // channel owns (items, unwatched bodies).
        private static readonly List<string> _offeredDeltas = new List<string>();
        private static readonly HashSet<string> _offeredBodies = new HashSet<string>();

        /// <summary>"Cryo up 15, now 21." — composed exactly as CS1's
        /// ResourceWatch line, from the watched change the vitals channel
        /// handed over instead of discarding under the two-lane rule.
        /// Delta grammar (owner ruling 2026-08-02): a bare change is
        /// adjustment + standing total; a change another factor caused
        /// carries its factor tag between them ("Stress down 2, focus,
        /// now 3 of 10.") — factors register via NoteFactor and are
        /// consumed by the next matching-direction delta.</summary>
        public static void OfferDelta(string label, float delta, string nowFormatted)
            => OfferDelta(label, label, delta, nowFormatted);

        /// <summary>canonical = the channel's generic name for effect-word
        /// matching (sync pass MED-4: the contract-stress channel offers under
        /// its rendered TITLE — the render-line dedupe needs the channel
        /// identity, not the label).</summary>
        public static void OfferDelta(string label, string canonical,
            float delta, string nowFormatted)
        {
            if (Mathf.Approximately(delta, 0f) || string.IsNullOrEmpty(label)) return;
            string sign = Lex.T(delta > 0 ? "vitals.up" : "vitals.down");
            string factor = null;
            if (delta < 0 && _factors.TryGetValue(label.ToUpperInvariant(), out factor))
                _factors.Remove(label.ToUpperInvariant());
            int amount = Mathf.Abs(Mathf.RoundToInt(delta));
            if (amount == 0) amount = 1; // clamped fraction: never "up 0"
            _offeredDeltas.Add(label + " " + sign + " " + amount
                + (factor != null ? ", " + factor : "")
                + ", " + Lex.T("outcome.now") + " " + nowFormatted + ".");
            _offeredBodies.Add((canonical ?? label).ToUpperInvariant());
        }

        // Factor tags for the delta grammar (the deferred FOCUS heal is the
        // first customer: PushFlow notes "focus" for Stress when the Action
        // Controller's own heal rung fires during a resolution). Keyed by
        // label; consumed once; cleared at each new outcome signal.
        private static readonly Dictionary<string, string> _factors =
            new Dictionary<string, string>();

        /// <summary>An attributing factor for the next negative delta of this
        /// label within the current resolution.</summary>
        public static void NoteFactor(string label, string factor)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(factor)) return;
            _factors[label.ToUpperInvariant()] = factor;
        }
        private static Dictionary<Transform, string> _clockSnapshot;
        private static float _lastReadAt = -10f;

        /// <summary>The outcome lane is speaking (or about to): Vitals stands down
        /// and caches silently (two-lane rule).</summary>
        public static bool ResolutionInFlight
            => Pendings.Count > 0 || Time.unscaledTime < _lastReadAt + 1.5f;

        /// <summary>A capture is genuinely open to consume offered deltas (sync
        /// pass HIGH-2: a delta offered during the post-read TAIL had no
        /// consumer and was cleared unspoken at the next capture — the tail
        /// suppresses focus flurries, never the hand-off).</summary>
        public static bool CapturePending => Pendings.Count > 0;

        public static void Init()
        {
            // Pre-roll snapshot: the first state past ActionStart, both families.
            FsmSignals.Subscribe(null, "Stress Setup", (fsm, s) => Snapshot(fsm));
            FsmSignals.Subscribe(null, "Working", (fsm, s) => Snapshot(fsm));

            SubscribeOutcome("Positive Outcome", "outcome.positive", "Positive");
            SubscribeOutcome("Neutral Outcome", "outcome.neutral", "Neutral");
            SubscribeOutcome("Negative Outcome", "outcome.negative", "Negative");
            // The deterministic family's bare Outcome renders no tier word —
            // name only (render-honesty; CS1 BL-14 carried). Its single row
            // is the Neutral one (D14 §1).
            SubscribeOutcome("Outcome", null, "Neutral");

            // The content read's clock is the settle WATCH (Tick) — rest events
            // retired 2026-08-01: the rest-event read raced the 0.17s toast
            // fade (the retry it forced was a timer), and the big-card family
            // never rests at all; the render/write moments cover both families
            // by construction.
        }

        private static void SubscribeOutcome(string state, string tierKey, string rowName)
        {
            FsmSignals.Subscribe(null, state, (fsm, s) => Capture(fsm, tierKey, rowName));
        }

        private static bool IsController(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject == null) return false;
            var id = fsm.FsmVariables.GetFsmString("Action Identifier");
            return id != null && !string.IsNullOrEmpty(id.Value);
        }

        private static void Snapshot(PlayMakerFSM fsm)
        {
            if (!IsController(fsm)) return;
            _clockSnapshot = LocationTable.ClockSnapshot();
        }

        private static void Capture(PlayMakerFSM fsm, string tierKey, string rowName)
        {
            if (!IsController(fsm)) return;
            var card = fsm.transform.parent;
            string name = card != null
                ? (Describe.TextUnder(card, "Action Name") ?? card.name.TrimEnd())
                : fsm.gameObject.name;
            SpeechService.Say(tierKey != null
                    ? name + ": " + Lex.T(tierKey)
                    : name + ".",
                Priority.Queued, "outcome");
            // Factors are NOT cleared here (sync pass HIGH-2): the heal rung
            // fires immediately BEFORE the outcome state, so its factor must
            // survive this signal to meet its delta during the settle window.
            // ReadContent clears them at composition end.
            _offeredDeltas.Clear();
            _offeredBodies.Clear();
            Pendings[fsm] = new Pending
            {
                Name = name,
                Row = rowName,
                Card = card,
                GroupParent = card != null && card.parent != null ? card.parent.parent : null,
                Deadline = Time.unscaledTime + 2f,
            };
        }

        /// <summary>The settle watch: per frame, a pending reads the MOMENT its
        /// card reaches a settle condition — all toast rows drawn (the render
        /// moment; an active-but-fading row parks the read), or no toast rows
        /// at all by the second sighting (the write moment). Speech itself is
        /// queued instantly either way; nothing here holds an utterance. The
        /// deadline is the fail-loud backstop for a card whose rows never all
        /// draw — a capture, never the path.</summary>
        public static void Tick()
        {
            if (Pendings.Count == 0) return;
            List<PlayMakerFSM> due = null;
            foreach (var kv in Pendings)
            {
                var p = kv.Value;
                p.SeenTicks++;
                var card = p.Card != null && p.Card.gameObject.activeInHierarchy
                    ? p.Card
                    : ReFind(p.GroupParent, p.Name);
                bool ready;
                if (card == null)
                {
                    // Nothing observable to wait on (variant swap mid-fade):
                    // read the narrative-silent composition now.
                    ready = p.SeenTicks >= 2;
                }
                else
                {
                    var scope = RowScope(card, p.Row);
                    if (scope != null)
                    {
                        // The achieved tier's own row: all its lines share one
                        // CanvasGroup, so they draw together 1-3 frames into
                        // the 0.17s fade — the settle lands there, not at 2s.
                        CountToasts(scope, card, out int active, out int drawn);
                        ready = active > 0 ? drawn == active : p.SeenTicks >= 2;
                    }
                    else
                    {
                        // Unknown row variant (loudly logged): whole-container
                        // drawn==active is UNSATISFIABLE (losing tiers +
                        // PREDICTIVE hold active alpha-0 lines) — any drawn
                        // NON-PREDICTIVE toast settles; a card with no active
                        // toasts reads at the write moment. PREDICTIVE is
                        // excluded from the census: with the INTUIT perk it
                        // rides at alpha 1 and would false-settle at tick 1
                        // (sync pass MED-7).
                        LogOnceOutcome("[Outcome] no '" + p.Row + "' row under OUTCOMES on '"
                            + p.Name + "' — fallback settle (capture)");
                        var outcomes = FindOutcomes(card);
                        CountToasts(outcomes ?? card, card, out int active, out int drawn,
                            excludePredictive: true);
                        ready = drawn > 0 || (active == 0 && p.SeenTicks >= 2);
                    }
                }
                if (!ready && Time.unscaledTime >= p.Deadline)
                {
                    Plugin.Log.LogWarning("[Outcome] '" + p.Name
                        + "' hit the settle backstop — toast rows never all drew (capture)");
                    ready = true;
                }
                if (ready) (due = due ?? new List<PlayMakerFSM>()).Add(kv.Key);
            }
            if (due == null) return;
            foreach (var fsm in due)
            {
                var p = Pendings[fsm];
                Pendings.Remove(fsm);
                ReadContent(p);
            }
        }

        /// <summary>The achieved tier's row under OUTCOMES (decode 2026-08-02
        /// — the settle/effect scope; the container's OTHER content is latent
        /// chrome: losing tiers preset with real +/- text at Outcome Setup,
        /// PREDICTIVE hidden by alpha only). Null = unknown variant.</summary>
        private static Transform RowScope(Transform card, string rowName)
        {
            if (card == null || rowName == null) return null;
            var outcomes = FindOutcomes(card);
            return outcomes != null ? outcomes.Find(rowName) : null;
        }

        /// <summary>+/- toast census over the given scope. active = toast
        /// lines the capture will want, drawn = lines it can already see.</summary>
        private static void CountToasts(Transform scope, Transform card,
            out int active, out int drawn, bool excludePredictive = false)
        {
            active = 0; drawn = 0;
            foreach (var tmp in scope.GetComponentsInChildren<TMP_Text>(false))
            {
                if (excludePredictive && UnderPredictive(tmp.transform, scope)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text)) continue;
                if (text[0] != '+' && text[0] != '-') continue;
                active++;
                if (Util.RenderedUp(tmp.transform, card)) drawn++;
            }
        }

        private static bool UnderPredictive(Transform t, Transform stopAt)
        {
            for (var cur = t; cur != null && cur != stopAt; cur = cur.parent)
                if (cur.name == "PREDICTIVE") return true;
            return false;
        }

        private static readonly HashSet<string> _outcomeLogged = new HashSet<string>();

        private static void LogOnceOutcome(string line)
        {
            if (_outcomeLogged.Add(line)) Plugin.Log.LogWarning(line);
        }

        private static Transform FindOutcomes(Transform card)
        {
            var direct = card.Find("OUTCOMES");
            if (direct != null) return direct;
            foreach (var t in card.GetComponentsInChildren<Transform>(true))
                if (t.name == "OUTCOMES") return t;
            return null;
        }

        private static void ReadContent(Pending p)
        {
            var card = p.Card != null && p.Card.gameObject.activeInHierarchy
                ? p.Card
                : ReFind(p.GroupParent, p.Name);

            // Settled read (owner ruling): everything — narrative, effect lines,
            // totals — from the card as it now stands. The settle watch already
            // guaranteed every active toast row is drawn, so the capture sees
            // the full set; the old post-hoc retry timer is gone by construction.
            var effects = card != null ? CaptureEffects(card, p.Row) : null;
            _lastReadAt = Time.unscaledTime;

            var sb = new System.Text.StringBuilder();
            if (card != null)
            {
                string narrative = Describe.TextUnder(card, "Description");
                if (narrative != null) sb.Append(narrative);
            }
            else
            {
                Plugin.Log.LogInfo("[Outcome] card for '" + p.Name
                    + "' gone and not re-found in the active variant — narrative silent.");
            }
            // Watched-state deltas FIRST (the CS1 ResourceWatch composition —
            // amount + new total, handed over by the standing-down channels).
            foreach (var line in _offeredDeltas)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(line);
            }
            if (effects != null)
            {
                if (effects.Count == 0 && _offeredDeltas.Count == 0)
                    Plugin.Log.LogInfo("[Outcome] '" + p.Name
                        + "' settled with no effects from render OR watch.");
                foreach (var (delta, bodyWord) in effects)
                {
                    // Render lines only for bodies no watched channel already
                    // spoke — matched by CHANNEL identity, longest name wins
                    // (sync pass MED-4: "+2 CONTRACT STRESS" contains "STRESS"
                    // and the player channel wrongly claimed it).
                    string canon = Vitals.CanonicalFor(bodyWord);
                    if (canon != null && _offeredBodies.Contains(canon.ToUpperInvariant()))
                        continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(delta);
                    // Movement only, no standing total (owner ruling 2026-08-04):
                    // the chip names a bar in the card's vocabulary, which cannot
                    // say WHICH instance moved, so any total appended here risked
                    // being another bar's. The watched delta above carries the
                    // total, read off the bar that actually moved.
                    sb.Append('.');
                }
            }
            _offeredDeltas.Clear();
            _offeredBodies.Clear();
            _factors.Clear();
            foreach (var change in LocationTable.ClockChanges(_clockSnapshot))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(change);
            }
            _clockSnapshot = null;
            if (sb.Length > 0)
                SpeechService.Say(sb.ToString(), Priority.Queued, "outcome");
        }

        /// <summary>Rendered effect lines, captured at the outcome signal: every
        /// visible text on the card leading with a +/- glyph run, transcoded
        /// (single glyph = direction + body, multiple = direction + count + body
        /// — the outcome-effect policy over the shared GlyphRun parse, ruling A9).
        /// Returns (spoken delta, body word) pairs; the body word keys the
        /// rest-time bar-state suffix.</summary>
        private static List<(string delta, string body)> CaptureEffects(
            Transform card, string rowName)
        {
            var effects = new List<(string, string)>();
            // OUTCOMES-internal content outside the achieved row is latent
            // chrome (decode 2026-08-02): PREDICTIVE duplicates the outcome
            // lines and rides at alpha 1 whenever the INTUIT perk is owned —
            // whole-card capture double-read them. Card surfaces outside the
            // container (COST etc.) still capture.
            var outcomes = FindOutcomes(card);
            var row = RowScope(card, rowName);
            foreach (var tmp in card.GetComponentsInChildren<TMP_Text>(false))
            {
                if (outcomes != null && tmp.transform.IsChildOf(outcomes))
                {
                    if (row != null && !tmp.transform.IsChildOf(row)) continue;
                    // Unknown-row fallback still excludes PREDICTIVE — its
                    // perk-alpha duplicates every tier's lines (MED-7).
                    if (row == null && UnderPredictive(tmp.transform, outcomes)) continue;
                }
                if (!Util.RenderedUp(tmp.transform, card)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text)) continue;
                if (text[0] != '+' && text[0] != '-') continue;
                int body = Util.GlyphRun(text, out int plus, out int minus);
                string rest = text.Substring(body).Trim();
                if (rest.Length == 0) continue;
                string delta;
                if (plus > 0 && minus == 0)
                    delta = Lex.T("glyph.plus") + (plus > 1 ? " " + plus : "") + " " + rest;
                else if (minus > 0 && plus == 0)
                    delta = Lex.T("glyph.minus") + (minus > 1 ? " " + minus : "") + " " + rest;
                else
                    delta = text; // mixed run: spoken raw, never guessed
                effects.Add((delta, rest));
            }
            return effects;
        }

        private static Transform ReFind(Transform groupParent, string name)
        {
            if (groupParent == null) return null;
            foreach (Transform group in groupParent)
            {
                if (!group.gameObject.activeInHierarchy) continue;
                foreach (Transform card in group)
                {
                    if (!card.gameObject.activeInHierarchy) continue;
                    string cardName = Describe.TextUnder(card, "Action Name");
                    if (cardName != null && cardName == name) return card;
                }
            }
            return null;
        }
    }
}
