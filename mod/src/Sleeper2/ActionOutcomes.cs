using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Action resolution announcements (owner direction 2026-07-31; CS1
    /// ActionOutcomes is the baseline, re-derived not copied): the tier speaks at
    /// the outcome signal, then ONE deferred, composed reading of the card's fresh
    /// narrative and every clock whose rendered progress moved.
    ///
    /// CS2 deltas from the CS1 shape:
    ///  - Controller class = the Action Identifier variable (D2: 363/363 across
    ///    all variants) — CS1's controller-NAME list missed the cryo family for
    ///    weeks (its own F14 comment); the class predicate can't.
    ///  - Clock snapshot at the game's own pre-roll states (Stress Setup / the
    ///    repair family's Working, D11 §3) — commits run through the game's
    ///    button here, not a mod click, so there is no mod-side commit hook.
    ///  - The deferred read is ONE composed utterance (speech-code check, owner
    ///    direction): the modal gate's pen holds latest-per-source — four separate
    ///    lines under a mid-resolution tutorial would collapse to one; a composed
    ///    line survives whole. The tier line stays immediate-queued for
    ///    responsiveness and is the accepted pen casualty.
    ///  - Clock progress reads the RENDER (UICircle) throughout.
    ///
    /// Variant-swap survival carries from CS1: name and roots captured at the
    /// signal; if the card was torn down, the same rendered name is re-found in
    /// the active sibling group. Content stays rendered text (invariant 1).</summary>
    internal static class ActionOutcomes
    {
        private struct Pending
        {
            public string Name;
            public Transform Card;
            public Transform GroupParent;
            public float Due;
        }

        private static readonly List<Pending> Queue = new List<Pending>();
        private static Dictionary<Transform, string> _clockSnapshot;

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
            Queue.Add(new Pending
            {
                Name = name,
                Card = card,
                GroupParent = card != null && card.parent != null ? card.parent.parent : null,
                Due = Time.unscaledTime + 0.6f,
            });
        }

        public static void Tick()
        {
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                var p = Queue[i];
                if (Time.unscaledTime < p.Due) continue;
                Queue.RemoveAt(i);

                var card = p.Card != null && p.Card.gameObject.activeInHierarchy
                    ? p.Card
                    : ReFind(p.GroupParent, p.Name);

                // One composed utterance: fresh narrative, then every moved clock.
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
                foreach (var change in LocationTable.ClockChanges(_clockSnapshot))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(change);
                }
                _clockSnapshot = null;
                if (sb.Length > 0)
                    SpeechService.Say(sb.ToString(), Priority.Queued, "outcome");
            }
        }

        /// <summary>A variant swap replaces the group but keeps the location
        /// parent: the same rendered name, re-found in the active sibling group
        /// (rendered names, never objects — CS1 idiom verbatim).</summary>
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
