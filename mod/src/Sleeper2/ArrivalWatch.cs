using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Where you are (2026-10-25): after loading a save or arriving from
    /// a journey nothing was spoken until the next key. The game's own global
    /// "Current Location Scene" names the place ("HUB - Hexport"); when it
    /// changes, the place is spoken once the player has the floor (station or
    /// rig), queued so it follows whatever the arrival itself says.</summary>
    internal static class ArrivalWatch
    {
        private const float PollEvery = 0.25f;
        private const float SettleAfter = 0.8f;

        private static float _nextPoll;
        private static string _last;
        private static string _pending;
        private static float _pendingSince;

        public static void Tick()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollEvery;

            if (GameQueries.TitleLive())
            {
                _last = null;
                _pending = null;
                return;
            }

            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .FindFsmString("Current Location Scene");
            string scene = v != null ? v.Value : null;
            if (!string.IsNullOrEmpty(scene) && scene != _last)
            {
                _last = scene;
                _pending = scene;
                _pendingSince = Time.unscaledTime;
            }

            if (_pending == null || Time.unscaledTime - _pendingSince < SettleAfter) return;
            var mode = ModeModel.Current();
            if (mode != Mode.Station && mode != Mode.RigRooms) return;
            string place = PlaceName(_pending);
            _pending = null;
            if (string.IsNullOrEmpty(place)) return;
            Plugin.Log.LogInfo("[Arrival] " + place);
            SpeechService.Say(place + ".", Priority.Queued, "arrival");
        }

        /// <summary>"HUB - Hexport" -> "Hexport": the scene kind before the dash is
        /// the game's bookkeeping, the name after it is the place.</summary>
        private static string PlaceName(string scene)
        {
            int dash = scene.IndexOf(" - ", System.StringComparison.Ordinal);
            string name = dash >= 0 ? scene.Substring(dash + 3) : scene;
            return SpeechService.Clean(name);
        }
    }
}
