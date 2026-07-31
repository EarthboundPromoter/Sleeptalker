using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Dialogue skill checks (owner-ruled 2026-07-26): odds on hover,
    /// outcome on resolution.
    ///
    /// Odds: hovering a skill-check response drives its FSM MouseOver -> "% Display"
    /// -> a tier state (Player NEG/NEU/POS/BOON) whose OnEnter activates the game's
    /// percentage bucket objects under "Response Text/DICE percentages" ("50% POS"
    /// etc.). We subscribe to the tier states and read the rendered texts, expanding
    /// the game's closed abbreviation set (POS/NEU/NEG — byte-scan verified total
    /// over all game data) to the full words the game itself uses in its long-form
    /// tutorial text. Gamepad focus fires MouseOver, live-verified.
    ///
    /// Resolution: the roll result renders only as a wordless dice badge — the
    /// Response Manager FSM activates objects named Positive/Neutral/Negative (no
    /// text anywhere in them), so the tier is announced from the manager's state
    /// dial, worded by the game's own badge names. Its per-path stress ops
    /// (Result: FloatAdd Player_Stress, Result 3: FloatSubtract) carry the check's
    /// stress delta in the manager's "Stress" variable; a nonzero delta gates the
    /// stress line. The narrative consequence follows as a normal dialogue line.</summary>
    internal static class SkillChecks
    {
        private static readonly Regex OddsPattern =
            new Regex(@"^(\d{1,3})% ?(POS|NEU|NEG)$", RegexOptions.Compiled);

        public static void Init()
        {
            foreach (var tier in new[] { "Player NEG", "Player NEU", "Player POS", "Player BOON" })
                FsmSignals.Subscribe(null, tier, OnOddsShown);

            // Result = the negative path (Neg -> Wait -> Result), Result 2 neutral,
            // Result 3 positive — strictly separated paths, census-verified.
            FsmSignals.Subscribe("Response Manager", "Result", (fsm, s) => OnResolved(fsm, "check.negative", +1));
            FsmSignals.Subscribe("Response Manager", "Result 2", (fsm, s) => OnResolved(fsm, "check.neutral", 0));
            FsmSignals.Subscribe("Response Manager", "Result 3", (fsm, s) => OnResolved(fsm, "check.positive", -1));
        }

        /// <summary>A response button entered an odds tier state: speak the rendered
        /// percentage buckets. Only live responses ("Response: ...", the DS-renamed
        /// clones) qualify — the inactive per-skill templates never run.</summary>
        private static void OnOddsShown(PlayMakerFSM fsm, string state)
        {
            if (fsm == null || !fsm.gameObject.name.StartsWith("Response: ")) return;
            // Under a modal, this line is handled by the speech gate: "odds" is
            // registered volatile (the tutorial-vs-menu focus war re-fires
            // % Display in a loop, and dismissal re-fires the tier state fresh),
            // so no gating here — the surface stays modal-unaware by design.

            var container = fsm.transform.Find("Response Text/DICE percentages");
            if (container == null)
            {
                Plugin.Log.LogWarning("[SkillChecks] " + state + " on " + fsm.gameObject.name
                    + " but no DICE percentages child — hierarchy drifted?");
                return;
            }

            var sb = new StringBuilder();
            foreach (var tmp in container.GetComponentsInChildren<TMP_Text>(false))
            {
                string raw = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(raw)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Transcode(raw));
            }
            if (sb.Length == 0)
            {
                // The tier state activates the buckets in its OnEnter, so postfix
                // timing should always see them; silence here is capture-worthy.
                Plugin.Log.LogWarning("[SkillChecks] odds tier " + state + " on "
                    + fsm.gameObject.name + " rendered no bucket text.");
                return;
            }
            sb.Append('.');
            SpeechService.Say(sb.ToString(), Priority.Queued, "odds");
        }

        /// <summary>"50% POS" -> "50 percent positive". The abbreviation set is
        /// closed (0/20/25/50/100 POS, 50 NEU, 25/50/80 NEG as shipped); anything
        /// outside it is spoken raw and logged loudly, transcode-not-strip.</summary>
        private static string Transcode(string raw)
        {
            var m = OddsPattern.Match(raw);
            if (!m.Success)
            {
                Plugin.Log.LogWarning("[SkillChecks] UNMAPPED ODDS TEXT: \"" + raw + "\"");
                return raw;
            }
            string word;
            switch (m.Groups[2].Value)
            {
                case "POS": word = Lex.T("odds.positive"); break;
                case "NEU": word = Lex.T("odds.neutral"); break;
                default: word = Lex.T("odds.negative"); break;
            }
            return m.Groups[1].Value + " " + Lex.T("odds.percent") + " " + word;
        }

        /// <summary>The check resolved: announce the tier, then the stress movement
        /// when this path actually carries one (the manager's Stress variable is the
        /// delta its Result state applies to Player_Stress; zero means no change).</summary>
        private static void OnResolved(PlayMakerFSM fsm, string tierKey, int stressSign)
        {
            string line = Lex.T(tierKey);
            if (stressSign != 0 && fsm != null)
            {
                var delta = fsm.FsmVariables.GetFsmFloat("Stress");
                if (delta != null && delta.Value != 0f)
                    line += " " + Lex.T(stressSign > 0 ? "check.stress-up" : "check.stress-down");
            }
            SpeechService.Say(line, Priority.Queued, "check");
        }
    }
}
