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
