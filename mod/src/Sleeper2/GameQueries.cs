using UnityEngine;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>CS2 game reads — the Sleeper2 tier's FSM toolbox. Skeleton stage:
    /// FSM lookup and input-mode enforcement only; dice/clock/vitals reads arrive
    /// with their surfaces. Every name here is corpus-verified (static census
    /// 2026-07-26 + live title probe).</summary>
    internal static class GameQueries
    {
        /// <summary>First FSM whose owner GameObject bears this exact name; pathHint
        /// (substring of the full path) disambiguates when present.</summary>
        public static PlayMakerFSM FindFsm(string ownerName, string pathHint = null)
        {
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (fsm == null || fsm.gameObject == null) continue;
                if (!fsm.gameObject.scene.IsValid()) continue;
                if (fsm.gameObject.name != ownerName) continue;
                if (pathHint != null && !Util.PathOf(fsm.gameObject).Contains(pathHint)) continue;
                return fsm;
            }
            return null;
        }

        // ---------- Gamepad-mode enforcement (CS1 idiom; CS2 Gamepad Manager
        // live-verified at title: states Switch?/Gamepad/Mouse/Xbox?/PlayStation?,
        // events Gamepad/Mouse/PC/PlayStation/Switch/Xbox. Conservative port:
        // keyboard input flips Mouse -> Gamepad via the game's own event; the
        // platform-glyph states are left alone until live behavior is known. ----------

        public static void EnsureGamepadMode()
        {
            if (!Plugin.ForceGamepadUI.Value) return;
            var manager = FindFsm("Gamepad Manager");
            if (manager != null && manager.ActiveStateName == "Mouse")
            {
                manager.SendEvent("Gamepad");
                Plugin.Log.LogInfo("[Game] Keyboard input: asserted gamepad UI mode.");
            }
        }

        /// <summary>A mouse click claims mouse mode (cursor for a sighted co-pilot).
        /// The game's own Mouse event; if the current state has no transition for it,
        /// the event drops harmlessly.</summary>
        public static void EnsureMouseMode()
        {
            var manager = FindFsm("Gamepad Manager");
            if (manager != null && manager.ActiveStateName != "Mouse")
            {
                manager.SendEvent("Mouse");
                Plugin.Log.LogInfo("[Game] Mouse click: asserted mouse UI mode.");
            }
        }

        // ---------- Interim precedence dials (ride V1 state trap: the location
        // table owned the keys under pause AND under dice allocation — Action View?
        // stays true through both. These two dials are the stand-down set until the
        // ModeModel carries the full CS1 precedence table.) ----------

        private static Transform _pauseCanvas;
        private static PlayMakerFSM _diceSystem;

        /// <summary>Pause by render truth: the Pause Canvas's effective alpha (the
        /// canvas object stays in the scene; screens hide by alpha — founding CS2
        /// rule). Pause outranks every mod surface (CS1 precedence).</summary>
        public static bool Paused()
        {
            if (_pauseCanvas == null)
            {
                var go = GameObject.Find("Pause Canvas");
                _pauseCanvas = go != null ? go.transform : null;
                if (_pauseCanvas == null) return false;
            }
            return Util.AlphaUpTo(_pauseCanvas) >= 0.05f;
        }

        /// <summary>Dice allocation engaged: the Dice Gamepad System has left its
        /// Off resting state (live-verified Off at hub idle, §7b). While engaged,
        /// the game's own dice flow owns the keys; mod tables suspend (excursion,
        /// not exit). The proper allocation surface arrives with decode D11.</summary>
        public static bool DiceAllocationLive()
        {
            if (_diceSystem == null || _diceSystem.gameObject == null)
            {
                _diceSystem = FindFsm("Dice Gamepad System", "Top UI");
                if (_diceSystem == null) return false;
            }
            if (!_diceSystem.gameObject.activeInHierarchy) return false;
            string state = _diceSystem.ActiveStateName;
            return !string.IsNullOrEmpty(state) && state != "Off";
        }

        public static void InvalidateScene()
        {
            _pauseCanvas = null;
            _diceSystem = null;
        }
    }
}
