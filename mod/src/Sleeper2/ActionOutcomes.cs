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
    /// content read fires on the controller's OWN return-to-rest event — never a
    /// timer (owner correction; the game tells us when the card has settled:
    /// outcome states exit to Idle / Temp Complete / Action Completed, D2/D11).
    /// A deadline sweep survives a controller that never rests (logged, 2s).
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
            public Transform Card;
            public Transform GroupParent;
            public float Deadline;
            // One settle-retry (owner ruling + D14, 2026-08-01: effects are read
            // at the SETTLED state, never stashed at signal — the toast rows
            // fade in over 0.17s and hold ~4.3s, so signal-time capture read
            // alpha 0 and found nothing; there is no teardown to race).
            public bool Retried;
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
        /// handed over instead of discarding under the two-lane rule.</summary>
        public static void OfferDelta(string label, float delta, string nowFormatted)
        {
            if (Mathf.Approximately(delta, 0f) || string.IsNullOrEmpty(label)) return;
            string sign = Lex.T(delta > 0 ? "vitals.up" : "vitals.down");
            _offeredDeltas.Add(label + " " + sign + " "
                + Mathf.Abs(Mathf.RoundToInt(delta))
                + ", " + Lex.T("outcome.now") + " " + nowFormatted + ".");
            _offeredBodies.Add(label.ToUpperInvariant());
        }
        private static Dictionary<Transform, string> _clockSnapshot;
        private static float _lastReadAt = -10f;

        /// <summary>The outcome lane is speaking (or about to): Vitals stands down
        /// and caches silently (two-lane rule).</summary>
        public static bool ResolutionInFlight
            => Pendings.Count > 0 || Time.unscaledTime < _lastReadAt + 1.5f;

        public static void Init()
        {
            // Pre-roll snapshot: the first state past ActionStart, both families.
            FsmSignals.Subscribe(null, "Stress Setup", (fsm, s) => Snapshot(fsm));
            FsmSignals.Subscribe(null, "Working", (fsm, s) => Snapshot(fsm));

            SubscribeOutcome("Positive Outcome", "outcome.positive");
            SubscribeOutcome("Neutral Outcome", "outcome.neutral");
            SubscribeOutcome("Negative Outcome", "outcome.negative");
            // The deterministic family's bare Outcome renders no tier word —
            // name only (render-honesty; CS1 BL-14 carried).
            SubscribeOutcome("Outcome", null);

            // The content read's clock: the controller's own return to rest.
            foreach (var rest in new[] { "Idle", "Temp Complete", "Action Completed" })
                FsmSignals.Subscribe(null, rest, (fsm, s) => Resolve(fsm));
        }

        private static void SubscribeOutcome(string state, string tierKey)
        {
            FsmSignals.Subscribe(null, state, (fsm, s) => Capture(fsm, tierKey));
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

        private static void Capture(PlayMakerFSM fsm, string tierKey)
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
            _offeredDeltas.Clear();
            _offeredBodies.Clear();
            Pendings[fsm] = new Pending
            {
                Name = name,
                Card = card,
                GroupParent = card != null && card.parent != null ? card.parent.parent : null,
                Deadline = Time.unscaledTime + 2f,
            };
        }

        /// <summary>The controller rested: its card has rendered the outcome —
        /// read now (the actual event, no timer).</summary>
        private static void Resolve(PlayMakerFSM fsm)
        {
            if (fsm == null || !Pendings.TryGetValue(fsm, out var p)) return;
            Pendings.Remove(fsm);
            ReadContent(p);
        }

        /// <summary>Deadline sweep only — a controller that never rested is a
        /// capture, not a silent loss.</summary>
        public static void Tick()
        {
            if (Pendings.Count == 0) return;
            List<PlayMakerFSM> due = null;
            foreach (var kv in Pendings)
            {
                if (Time.unscaledTime < kv.Value.Deadline) continue;
                (due = due ?? new List<PlayMakerFSM>()).Add(kv.Key);
            }
            if (due == null) return;
            foreach (var fsm in due)
            {
                var p = Pendings[fsm];
                Pendings.Remove(fsm);
                // D14 correction: big cards REST IN their outcome state (no
                // exit to Idle) — the deadline IS the normal settle path.
                Plugin.Log.LogInfo("[Outcome] '" + p.Name
                    + "' settle read at deadline (rests-in-outcome family)");
                ReadContent(p);
            }
        }

        private static void ReadContent(Pending p)
        {
            var card = p.Card != null && p.Card.gameObject.activeInHierarchy
                ? p.Card
                : ReFind(p.GroupParent, p.Name);

            // Settled read (owner ruling): everything — narrative, effect lines,
            // totals — from the card AS IT RESTS. A fast-resting card can beat
            // the 0.17s toast fade-in once; retry the settle read a beat later
            // rather than speak an effect-less outcome (fail-loud on the second
            // miss: the log then says whether this action truly has no effects).
            var effects = card != null ? CaptureEffects(card) : null;
            if (card != null && (effects == null || effects.Count == 0) && !p.Retried)
            {
                var key = card.GetComponentInChildren<PlayMakerFSM>(true);
                if (key != null)
                {
                    p.Retried = true;
                    p.Deadline = Time.unscaledTime + 0.5f;
                    Pendings[key] = p; // the deadline sweep re-runs the settle read
                    return;
                }
            }
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
                        + "' settled with no effects from render OR watch (retried once).");
                foreach (var (delta, bodyWord) in effects)
                {
                    // Render lines only for bodies no watched channel already
                    // spoke (items and unwatched meters) — never double-tell.
                    bool covered = false;
                    string bodyUpper = bodyWord.ToUpperInvariant();
                    foreach (var owned in _offeredBodies)
                        if (bodyUpper.Contains(owned)) { covered = true; break; }
                    if (covered) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(delta);
                    string now = Vitals.CurrentFor(bodyWord);
                    if (now != null)
                        sb.Append(", ").Append(Lex.T("outcome.now")).Append(' ').Append(now);
                    sb.Append('.');
                }
            }
            _offeredDeltas.Clear();
            _offeredBodies.Clear();
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
        private static List<(string delta, string body)> CaptureEffects(Transform card)
        {
            var effects = new List<(string, string)>();
            foreach (var tmp in card.GetComponentsInChildren<TMP_Text>(false))
            {
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
