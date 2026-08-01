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
        // Negative caches (sync review LOW): a dial FSM absent from this scene is
        // absent until the next scene load — never a per-frame full-heap rescan.
        private static bool _mainMenuChecked, _pauseChecked, _mapChecked, _shipChecked, _cycleChecked;

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
            if (!_pauseChecked)
            {
                _pauseFsm = FindFsm("PAUSE");
                _pauseChecked = true;
            }
            if (_pauseFsm != null && _pauseFsm.gameObject != null)
            {
                // INIT is the boot state, not paused (D7: Idle/INIT = unpaused;
                // sync review MED-6).
                string state = _pauseFsm.ActiveStateName;
                return !string.IsNullOrEmpty(state) && state != "Idle" && state != "INIT";
            }
            if (_pauseCanvas == null)
            {
                var go = GameObject.Find("Pause Canvas");
                _pauseCanvas = go != null ? go.transform : null;
                if (_pauseCanvas == null) return false;
            }
            return Util.RenderedUp(_pauseCanvas);
        }

        /// <summary>Map open: the Map Screen root FSM in "Open" (D8; the top-bar
        /// button and Rewired "Map" both just send Open to it).</summary>
        public static bool MapOpen()
        {
            if (!_mapChecked)
            {
                _mapFsm = FindFsm("Map Screen");
                _mapChecked = true;
            }
            return _mapFsm != null && _mapFsm.gameObject != null
                && _mapFsm.ActiveStateName == "Open";
        }

        /// <summary>The Map Screen root transform (Belt Button lookup, sub-window
        /// walks). Null when the FSM is absent from the scene.</summary>
        public static Transform MapRoot()
        {
            if (!_mapChecked)
            {
                _mapFsm = FindFsm("Map Screen");
                _mapChecked = true;
            }
            return _mapFsm != null && _mapFsm.gameObject != null
                ? _mapFsm.transform : null;
        }

        /// <summary>A native map sub-window is up: Travel Confirm / Crew Window /
        /// "Crew Confrim" (sic — wart registry) / Leave Contract / No Pilot /
        /// Ship Damaged — all forced-focus dialogs, direct children of the Map
        /// Screen root, hidden by the alpha idiom (D8; the nested Abandon Window
        /// rides under Leave Contract). While one renders the map table stands
        /// down and the native focus reads carry it. Blockers first — they sit
        /// on top of everything.</summary>
        private static readonly string[] MapSubWindowsAll =
        {
            "No Pilot Window", "Ship Damaged Window",
            "Travel Confirm Window", "Leave Contract Window",
            "Crew Window", "Crew Confrim",
        };

        public static bool MapSubWindowUp()
        {
            var root = MapRoot();
            if (root == null) return false;
            foreach (var name in MapSubWindowsAll)
                if (ChildRendered(root, name) != null) return true;
            return false;
        }

        /// <summary>Map close: window-first Back ownership (sync review MED-10:
        /// a root Back under a live dialog would close the map beneath it).
        /// The crew stages belong to the Travel Confirm FSM — it owns every
        /// stage of the travel dialog and Back at any stage returns Back to
        /// the marker FSM (D8; sync pass F1 — the crew windows themselves
        /// carry no Back ownership). Blockers get Back on their own FSM; an
        /// unhandled event drops harmlessly (PlayMaker semantics, ride item).</summary>
        public static void MapBack()
        {
            var root = MapRoot();
            if (root == null)
            {
                Plugin.Log.LogWarning("[Game] MapBack: no Map Screen FSM found");
                return;
            }
            if (ChildRendered(root, "Crew Window") != null
                || ChildRendered(root, "Crew Confrim") != null)
            {
                var confirm = root.Find("Travel Confirm Window");
                var confirmFsm = confirm != null
                    ? confirm.GetComponent<PlayMakerFSM>() : null;
                if (confirmFsm != null) { confirmFsm.SendEvent("Back"); return; }
                Plugin.Log.LogWarning(
                    "[Game] MapBack: crew stage up but no Travel Confirm FSM — capture");
            }
            foreach (var name in MapSubWindowsAll)
            {
                var window = ChildRendered(root, name);
                if (window == null) continue;
                var fsm = window.GetComponent<PlayMakerFSM>();
                if (fsm == null)
                {
                    Plugin.Log.LogWarning("[Game] MapBack: window \"" + name
                        + "\" carries no FSM — capture");
                    continue;
                }
                fsm.SendEvent("Back");
                return;
            }
            if (_mapFsm != null) _mapFsm.SendEvent("Back");
        }

        private static Transform ChildRendered(Transform root, string name)
        {
            var t = root.Find(name);
            if (t == null || !t.gameObject.activeInHierarchy) return null;
            return Util.RenderedUp(t) ? t : null;
        }

        /// <summary>Rig side: the Ship UI toggle FSM in "Idle Ship" (D9 — the same
        /// button whose label flips RIG/EXPLORE; its own state is the dial).</summary>
        public static bool RigSide()
        {
            if (!_shipChecked)
            {
                _shipFsm = FindFsm("Ship UI", "Ship and Map Buttons");
                _shipChecked = true;
            }
            return _shipFsm != null && _shipFsm.gameObject != null
                && _shipFsm.ActiveStateName == "Idle Ship";
        }

        /// <summary>The Ship UI toggle's own button — the designed rig exit.</summary>
        public static GameObject ShipToggleButton()
        {
            if (!_shipChecked)
            {
                _shipFsm = FindFsm("Ship UI", "Ship and Map Buttons");
                _shipChecked = true;
            }
            if (_shipFsm == null || _shipFsm.gameObject == null) return null;
            var button = _shipFsm.transform.Find("Button");
            return button != null ? button.gameObject : null;
        }

        /// <summary>Cycle transitioning: the Cycle Controller off Idle. Its rest
        /// state has FOUR exits in CS2 (player end-cycle, travel, narrative
        /// auto-cycle, permadeath — D4); any departure is the transition wrapper.
        /// Quit is the parked end-dead screen, not a transition (sync review LOW —
        /// the mode must not wedge in CycleTransition at permadeath).</summary>
        public static bool CycleTransitioning()
        {
            if (!_cycleChecked)
            {
                _cycleFsm = FindFsm("Cycle Controller");
                _cycleChecked = true;
            }
            if (_cycleFsm == null || _cycleFsm.gameObject == null) return false;
            string state = _cycleFsm.ActiveStateName;
            return !string.IsNullOrEmpty(state)
                && state != "Idle" && state != "INIT" && state != "Quit";
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
            // Engaged = Active or Slotted ONLY (sync review MED-4): Setup is the
            // player system's own startState, Reselector the cancel-out beat, and
            // D4's cycle pipeline restarts the tray FSMs every cycle — counting
            // those flashed the mode mid-pipeline. The D11 hysteresis carries.
            var systems = DiceSystems();
            for (int i = 0; i < systems.Length; i++)
            {
                var fsm = systems[i];
                if (fsm == null || fsm.gameObject == null
                    || !fsm.gameObject.activeInHierarchy) continue;
                string state = fsm.ActiveStateName;
                if (state == "Active" || state == "Slotted") return true;
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
            _mainMenuChecked = _pauseChecked = _mapChecked = _shipChecked = _cycleChecked = false;
            _focusFsm = null;
            _focusChecked = false;
        }

        // ---------- Camera scroll accumulator (the CS1 map-table contract,
        // rebuilt on the live sweep 2026-07-31: hover never drove the camera —
        // the selector follows the CAMERA, so table follow writes the game's own
        // scroll input accumulator, Focus Z (+ damped follower + global), exactly
        // the value class CS1's table wrote. Supersedes the S3 never-write ruling
        // by owner direction; flagged in the reply for veto.) ----------

        private static PlayMakerFSM _focusFsm;
        private static bool _focusChecked;

        private static PlayMakerFSM FocusFsm()
        {
            if (!_focusChecked)
            {
                _focusFsm = FindFsm("Focus", "Focus Gimbal");
                _focusChecked = true;
            }
            return _focusFsm != null && _focusFsm.gameObject != null ? _focusFsm : null;
        }

        public static float? FocusZ()
        {
            var fsm = FocusFsm();
            var v = fsm != null ? fsm.FsmVariables.GetFsmFloat("Focus Z") : null;
            return v != null ? v.Value : (float?)null;
        }

        public static void SetFocusZ(float value)
        {
            var fsm = FocusFsm();
            if (fsm == null) return;
            var min = HutongGames.PlayMaker.FsmVariables.GlobalVariables.GetFsmFloat("Min Rotation");
            var max = HutongGames.PlayMaker.FsmVariables.GlobalVariables.GetFsmFloat("Max Rotation");
            value = Mathf.Clamp(value, min != null ? min.Value : -1400f,
                                       max != null ? max.Value : 3600f);
            var local = fsm.FsmVariables.GetFsmFloat("Focus Z");
            var damped = fsm.FsmVariables.GetFsmFloat("Damped Z");
            var global = HutongGames.PlayMaker.FsmVariables.GlobalVariables.GetFsmFloat("Focus Z Global");
            if (local != null) local.Value = value;
            if (damped != null) damped.Value = value;
            if (global != null) global.Value = value;
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
                if (!Util.RenderedUp(fsm.transform)) continue;
                if (fsm.FsmVariables.GetFsmFloat("ClockValue") == null
                    && fsm.FsmVariables.GetFsmInt("ClockValue") == null) continue;
                // The class is variable AND renderer (ride log 2026-07-31: marker
                // buttons and on/off switches carry ClockValue as consumers — the
                // residual zero-storm; only the object that DRAWS the clock counts).
                if (!HasUICircle(fsm.gameObject)) continue;
                return fsm;
            }
            return null;
        }

        private static bool HasUICircle(GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == "UICircle") return true;
            return false;
        }

        /// <summary>"x of y segments" — from the RENDER (owner ruling, ride V3:
        /// stop reading clock variables, they lie — ClockValue sat 0 on drawn
        /// clocks). The clock element draws with a UICircle component on the same
        /// GameObject as its FSM (live-verified; the glitch dials share the
        /// idiom): its Progress IS the drawn fill. X = the circle's progress
        /// (fraction × steps, or the raw count if it stores one), Y = the leading
        /// number of the clock's own name. The FSM variable path survives only as
        /// a logged fallback for a circle-less clock.</summary>
        public static string ClockProgress(PlayMakerFSM clockFsm)
        {
            if (clockFsm == null) return null;
            float steps = Util.LeadingInt(clockFsm.gameObject.name);

            float? drawn = UICircleProgress(clockFsm.gameObject);
            float value;
            if (drawn.HasValue)
            {
                // Fraction (0..1) scales to steps; a raw count passes through.
                value = drawn.Value <= 1.0001f && steps > 0f
                    ? Mathf.Round(drawn.Value * steps)
                    : Mathf.Round(drawn.Value);
                if (value < 0f) value = 0f;
            }
            else
            {
                var f = clockFsm.FsmVariables.GetFsmFloat("ClockValue");
                var i = clockFsm.FsmVariables.GetFsmInt("ClockValue");
                if (f == null && i == null) return null;
                value = f != null ? f.Value : i.Value;
                LogOnceGame("[Game] clock \"" + clockFsm.gameObject.name
                    + "\" has no UICircle — spoke ClockValue fallback (verify vs render)");
            }

            if (steps <= 0f)
            {
                LogOnceGame("[Game] clock \"" + clockFsm.gameObject.name
                    + "\" carries no step count in its name — bare value spoken.");
                return value.ToString("0");
            }
            return value.ToString("0") + " " + Scaffold.Lex.T("vitals.of")
                + " " + steps.ToString("0");
        }

        /// <summary>Reflection read of the UICircle's drawn progress (property or
        /// field, either casing) — the render value itself. Null when the object
        /// draws with something else. Raw values logged once per clock for the
        /// fraction-vs-count calibration.</summary>
        private static float? UICircleProgress(GameObject go)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var type = component.GetType();
                if (type.Name != "UICircle") continue;
                var prop = type.GetProperty("Progress") ?? type.GetProperty("progress");
                if (prop != null && prop.PropertyType == typeof(float))
                {
                    float raw = (float)prop.GetValue(component, null);
                    LogOnceGame("[Game] clock render \"" + go.name + "\" UICircle=" + raw);
                    return raw;
                }
                var field = type.GetField("Progress") ?? type.GetField("progress");
                if (field != null && field.FieldType == typeof(float))
                {
                    float raw = (float)field.GetValue(component);
                    LogOnceGame("[Game] clock render \"" + go.name + "\" UICircle=" + raw);
                    return raw;
                }
                LogOnceGame("[Game] UICircle on \"" + go.name
                    + "\" exposes no Progress member — capture needed");
            }
            return null;
        }

        private static readonly System.Collections.Generic.HashSet<string> _gameLogged =
            new System.Collections.Generic.HashSet<string>();

        private static void LogOnceGame(string line)
        {
            if (_gameLogged.Add(line)) Plugin.Log.LogInfo(line);
        }
    }
}
