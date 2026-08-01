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

        // ---------- Mode dials (decoded D4/D7/D8/D9/D11; consumed by ModeModel) ----------

        private static Transform _pauseCanvas;
        private static PlayMakerFSM[] _diceSystems;
        private static PlayMakerFSM _pauseFsm, _mapFsm, _shipFsm, _cycleFsm, _mainMenuFsm;
        private static bool _mainMenuChecked;

        /// <summary>Title scene: the MAIN MENU FSM exists (level0 only; its state is
        /// the screen dial TitleFlow/ClassSelect already ride).</summary>
        public static bool TitleLive()
        {
            if (!_mainMenuChecked)
            {
                _mainMenuFsm = FindFsm("MAIN MENU");
                _mainMenuChecked = true;
            }
            return _mainMenuFsm != null && _mainMenuFsm.gameObject != null;
        }

        /// <summary>Pause: the master PAUSE FSM off its Idle resting state (D7 —
        /// it owns Esc, timescale, and every submenu; ActiveStateName is the
        /// authoritative dial). Alpha read kept only as the missing-FSM fallback.</summary>
        public static bool Paused()
        {
            if (_pauseFsm == null || _pauseFsm.gameObject == null)
                _pauseFsm = FindFsm("PAUSE");
            if (_pauseFsm != null)
            {
                string state = _pauseFsm.ActiveStateName;
                return !string.IsNullOrEmpty(state) && state != "Idle";
            }
            if (_pauseCanvas == null)
            {
                var go = GameObject.Find("Pause Canvas");
                _pauseCanvas = go != null ? go.transform : null;
                if (_pauseCanvas == null) return false;
            }
            return Util.AlphaUpTo(_pauseCanvas) >= 0.05f;
        }

        /// <summary>Map open: the Map Screen root FSM in "Open" (D8; the top-bar
        /// button and Rewired "Map" both just send Open to it).</summary>
        public static bool MapOpen()
        {
            if (_mapFsm == null || _mapFsm.gameObject == null)
                _mapFsm = FindFsm("Map Screen");
            return _mapFsm != null && _mapFsm.ActiveStateName == "Open";
        }

        /// <summary>Map close: send the map root FSM its own Back event — exactly
        /// what the Back Button FSM fires off Rewired Back (D8).</summary>
        public static void MapBack()
        {
            if (_mapFsm == null || _mapFsm.gameObject == null)
                _mapFsm = FindFsm("Map Screen");
            if (_mapFsm != null) _mapFsm.SendEvent("Back");
            else Plugin.Log.LogWarning("[Game] MapBack: no Map Screen FSM found");
        }

        /// <summary>Rig side: the Ship UI toggle FSM in "Idle Ship" (D9 — the same
        /// button whose label flips RIG/EXPLORE; its own state is the dial).</summary>
        public static bool RigSide()
        {
            if (_shipFsm == null || _shipFsm.gameObject == null)
                _shipFsm = FindFsm("Ship UI", "Ship and Map Buttons");
            return _shipFsm != null && _shipFsm.ActiveStateName == "Idle Ship";
        }

        /// <summary>The Ship UI toggle's own button — the designed rig exit.</summary>
        public static GameObject ShipToggleButton()
        {
            if (_shipFsm == null || _shipFsm.gameObject == null)
                _shipFsm = FindFsm("Ship UI", "Ship and Map Buttons");
            if (_shipFsm == null) return null;
            var button = _shipFsm.transform.Find("Button");
            return button != null ? button.gameObject : null;
        }

        /// <summary>Cycle transitioning: the Cycle Controller off Idle. Its rest
        /// state has FOUR exits in CS2 (player end-cycle, travel, narrative
        /// auto-cycle, permadeath — D4); any departure is the transition wrapper.</summary>
        public static bool CycleTransitioning()
        {
            if (_cycleFsm == null || _cycleFsm.gameObject == null)
                _cycleFsm = FindFsm("Cycle Controller");
            if (_cycleFsm == null) return false;
            string state = _cycleFsm.ActiveStateName;
            return !string.IsNullOrEmpty(state) && state != "Idle";
        }

        /// <summary>Travel ride: Current Location Scene prefix "TRN" (D9 — the
        /// master scene dial, stamped by every scene's setup FSM).</summary>
        public static bool TravelScene()
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmString("Current Location Scene");
            return v != null && v.Value != null && v.Value.StartsWith("TRN");
        }

        /// <summary>Dice allocation engaged: ANY of the three gamepad dice systems
        /// (player + Crew 1/2 — D11: a card's slot activates all three together)
        /// off its Off resting state. INIT is the crew clones' boot self-activate,
        /// not engagement. While engaged, the game's own dice flow owns the keys;
        /// mod tables suspend (excursion, not exit).</summary>
        public static bool DiceAllocationLive()
        {
            var systems = DiceSystems();
            for (int i = 0; i < systems.Length; i++)
            {
                var fsm = systems[i];
                if (fsm == null || fsm.gameObject == null
                    || !fsm.gameObject.activeInHierarchy) continue;
                string state = fsm.ActiveStateName;
                if (!string.IsNullOrEmpty(state) && state != "Off" && state != "INIT")
                    return true;
            }
            return false;
        }

        /// <summary>The three gamepad dice systems (player, Crew 1, Crew 2), cached.
        /// Missing crew systems stay null (template grace).</summary>
        public static PlayMakerFSM[] DiceSystems()
        {
            if (_diceSystems == null
                || _diceSystems[0] == null || _diceSystems[0].gameObject == null)
            {
                _diceSystems = new[]
                {
                    FindFsm("Dice Gamepad System", "Top UI"),
                    FindFsm("Crew 1 Dice Gamepad System"),
                    FindFsm("Crew 2 Dice Gamepad System"),
                };
            }
            return _diceSystems;
        }

        public static void InvalidateScene()
        {
            _pauseCanvas = null;
            _diceSystems = null;
            _pauseFsm = null;
            _mapFsm = null;
            _shipFsm = null;
            _cycleFsm = null;
            _mainMenuFsm = null;
            _mainMenuChecked = false;
        }

        // ---------- Clocks (the ClockValue FSM class) ----------
        // Census 2026-07-31: 2,823 FSMs across 32 levels carry the variable
        // ClockValue — "N Step Clock" (2,439), "N Step Accruing Clock" (192),
        // "N Step Clock (Clamped Cycle Discovered)" (192), plus the zone billboard
        // Setter/Updating Clock family. Owner ruling (CS1 lesson relearned): the
        // CLASS is the variable, never the name — name-keyed detection missed the
        // accruing/clamped variants entirely. Steps have no variable; the game
        // authors them as the leading number of the class FSM's own owner name,
        // uniform across all variants.

        /// <summary>The RENDERED clock-class FSM under a card/billboard element, by
        /// the ClockValue variable it must carry. Clock cards hold dormant sibling
        /// variant FSMs (a 3-step accruing AND an 8-step clamped under one card —
        /// census; ride V1's zero-storm read them), so only an ACTIVE, effectively
        /// VISIBLE carrier counts. Null = no rendered clock here.</summary>
        public static PlayMakerFSM ClockFsm(Transform root)
        {
            if (root == null) return null;
            foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(false))
            {
                if (!fsm.gameObject.activeInHierarchy) continue;
                if (Util.AlphaUpTo(fsm.transform) < 0.05f) continue;
                if (fsm.FsmVariables.GetFsmFloat("ClockValue") != null
                    || fsm.FsmVariables.GetFsmInt("ClockValue") != null)
                    return fsm;
            }
            return null;
        }

        /// <summary>"x of y" from a clock-class FSM: value = its own ClockValue,
        /// steps = the leading number of its owner name. Bare value (logged once)
        /// when the name carries no count.</summary>
        public static string ClockProgress(PlayMakerFSM clockFsm)
        {
            if (clockFsm == null) return null;
            float value;
            var f = clockFsm.FsmVariables.GetFsmFloat("ClockValue");
            if (f != null) value = f.Value;
            else
            {
                var i = clockFsm.FsmVariables.GetFsmInt("ClockValue");
                if (i == null) return null;
                value = i.Value;
            }
            float steps = Util.LeadingInt(clockFsm.gameObject.name);
            if (steps <= 0f)
            {
                if (_stepless.Add(clockFsm.gameObject.name))
                    Plugin.Log.LogWarning("[Game] clock FSM \"" + clockFsm.gameObject.name
                        + "\" carries no step count in its name — bare value spoken.");
                return value.ToString("0.#");
            }
            return value.ToString("0.#") + " " + Scaffold.Lex.T("vitals.of")
                + " " + steps.ToString("0");
        }

        private static readonly System.Collections.Generic.HashSet<string> _stepless =
            new System.Collections.Generic.HashSet<string>();
    }
}
