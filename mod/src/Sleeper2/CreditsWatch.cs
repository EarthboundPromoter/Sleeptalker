using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The credits (2026-10-25; the CS1 EndgameWatch idea for CS2's own
    /// CREDITS scene). The scene scrolls one long credits text and offers a SKIP
    /// button whose prompt is the gamepad's B; nothing is selected, so neither
    /// the keyboard nor the mod could reach it and the screen was silent. On
    /// arrival the screen is named with its keys; Enter or Backspace clicks the
    /// game's own SKIP button (live test: the Credits Canvas FSM leaves Wait and
    /// loads the title); Space reads the credits text.</summary>
    internal static class CreditsWatch
    {
        private const string SceneName = "CREDITS";
        private static bool _announced;

        private static bool InCredits()
            => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneName;

        public static void Tick()
        {
            if (!InCredits()) { _announced = false; return; }
            if (_announced) return;
            _announced = true;
            SpeechService.Say(Lex.T("credits.intro"), Priority.Queued, "credits");
        }

        /// <summary>InputManager, before anything else: true when the key was the
        /// credits screen's.</summary>
        public static bool HandleKeys()
        {
            if (!InCredits()) return false;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Backspace))
            {
                var skip = GameObject.Find("Credits Canvas/Skip Canvas/SKIP");
                if (skip != null && skip.activeInHierarchy)
                {
                    SpeechService.Say(Lex.T("credits.skipping"), Priority.Immediate, "credits");
                    Navigator.Click(skip);
                }
                else
                    SpeechService.Say(Lex.T("credits.no-skip"), Priority.Immediate, "credits");
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                var text = GameObject.Find("Credits Canvas/Credits");
                var tmp = text != null ? text.GetComponent<TMPro.TMP_Text>() : null;
                string body = tmp != null ? SpeechService.Clean(tmp.text) : null;
                SpeechService.Say(body ?? Lex.T("credits.intro"), Priority.Immediate, "credits");
                return true;
            }
            return false;
        }
    }
}
