using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Title arrival announcements (CS1 TitleFlow's job, minimal CS2 form).
    /// The boot-sweep rule rightly silences game-driven focus before first input —
    /// but that leaves the press-start gate focused and inaudible (live finding,
    /// first CS2 boot). The designed channel: announce the landing itself, clocked
    /// on the MAIN MENU FSM's own Landing entry, speaking the settled selection.</summary>
    internal static class TitleFlow
    {
        private const float AnnounceDefer = 0.6f;

        private static float _pendingAt = -1f;

        public static void Init()
        {
            FsmSignals.Subscribe("MAIN MENU", "Landing",
                (fsm, state) => _pendingAt = Time.unscaledTime + AnnounceDefer);
        }

        // The title menus' own back (2026-10-25): the MAIN MENU FSM's Back event,
        // which the game reads from the Rewired "Back" action (right mouse and
        // gamepad only), so keyboard players could not step back. Only the states
        // where Back is a menu step (live transition dump: Class Select ->
        // Difficulty Select -> New Game -> Demo Menu; Warning N -> New Game;
        // Continue and Language -> Demo Menu), never where it means a failed load.
        private static readonly string[] BackStates =
        {
            "Class Select", "Difficulty Select", "New Game", "Continue",
            "Warning 1", "Warning 2", "Warning 3", "Language",
        };

        public static bool Back()
        {
            var menu = GameQueries.FindFsm("MAIN MENU");
            if (menu == null) return false;
            if (System.Array.IndexOf(BackStates, menu.ActiveStateName) < 0) return false;
            // The menu it lands on selects its first button inside the event: that
            // landing is the answer to this key, so it speaks.
            FocusPatch.NoteUserNavigation();
            menu.SendEvent("Back");
            return true;
        }

        public static void Tick()
        {
            if (_pendingAt < 0 || Time.unscaledTime < _pendingAt) return;
            _pendingAt = -1f;
            var selected = Navigator.Current();
            string element = selected != null ? ElementDescriber.Element(selected, false) : null;
            SpeechService.Say(
                element != null ? "Title screen. " + element + "." : "Title screen.",
                Priority.Queued, "title");
        }
    }
}
