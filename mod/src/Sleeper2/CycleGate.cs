using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The cycle gate (owner direction 2026-07-31; CS1 CycleGate
    /// re-derived on D4): arms when the Cycle Controller leaves Idle by ANY of
    /// its four CS2 exits (player end-cycle, travel, narrative auto-cycle,
    /// permadeath — the CS1 single-exit premise is void), and on the armed
    /// return to Idle speaks ONE composed wake-up summary — absolute totals, no
    /// deltas (CS1 ruling): cycle number, every live vitals channel, the dice
    /// tray (glitch/broken states included — CS2 additions), and crew dice.
    ///
    /// While armed (plus a settle grace), the vitals channels cache silently —
    /// the summary IS the wake readout (two-lane rule, third customer). The
    /// focus flurry is already suppressed by FocusPatch off the same dial.
    ///
    /// Cycle number is the controller's own Cycle Count — the practical lane:
    /// no rendered cycle number exists in CS2 (D15 open question; promotes to a
    /// render read if a ride ever finds one).</summary>
    internal static class CycleGate
    {
        private static bool _armed;
        private static float _settledAt = -10f;

        /// <summary>Vitals stand down while the transition runs (+1s grace for
        /// the post-unfade bar animations).</summary>
        public static bool TransitionInFlight
            => _armed || Time.unscaledTime < _settledAt + 1f;

        public static void Init()
        {
            FsmSignals.Subscribe("Cycle Controller", null, (fsm, state) =>
            {
                if (!_armed)
                {
                    if (state == "Cycle" || state == "Travel Cycle"
                        || state == "Scene Hold" || state == "End")
                        _armed = true;
                    return;
                }
                if (state != "Idle") return;
                _armed = false;
                _settledAt = Time.unscaledTime;
                Summarize(fsm);
            });
        }

        private static void Summarize(PlayMakerFSM controller)
        {
            var sb = new System.Text.StringBuilder(Lex.T("cycle.prefix"));
            var count = controller != null
                ? controller.FsmVariables.GetFsmFloat("Cycle Count") : null;
            var countInt = controller != null
                ? controller.FsmVariables.GetFsmInt("Cycle Count") : null;
            if (count != null) sb.Append(' ').Append(count.Value.ToString("0"));
            else if (countInt != null) sb.Append(' ').Append(countInt.Value);
            sb.Append('.');

            foreach (var line in Vitals.Read())
                sb.Append(' ').Append(line);

            bool anyDie = false;
            for (int n = 1; n <= 5; n++)
            {
                string die = DiceFlow.DieLine(n);
                if (die == null) continue;
                if (!anyDie) { sb.Append(' ').Append(Lex.T("cycle.dice")); anyDie = true; }
                sb.Append(' ').Append(die);
            }
            for (int member = 1; member <= 2; member++)
                for (int slot = 1; slot <= 2; slot++)
                {
                    string die = DiceFlow.CrewDieLine(member, slot);
                    if (die != null) sb.Append(' ').Append(die);
                }

            SpeechService.Say(sb.ToString(), Priority.Queued, "cycle");
        }
    }
}
