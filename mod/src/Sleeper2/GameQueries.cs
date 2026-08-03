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
        // TOPMOST-first: blockers over everything, then the travel chain in
        // REVERSE stage order — later stages overlay earlier ones and the
        // earlier windows can stay rendered underneath (ride capture
        // 2026-08-02: Crew Confrim opened over a still-active Crew Window and
        // the swap announce never fired while Crew Window preceded it here).
        private static readonly string[] MapSubWindowsAll =
        {
            "No Pilot Window", "Ship Damaged Window",
            "Crew Confrim", "Crew Window",
            "Leave Contract Window", "Travel Confirm Window",
        };

        public static bool MapSubWindowUp() => MapSubWindow() != null;

        /// <summary>The topmost rendered map sub-window (the MapSubWindowsAll
        /// order — blockers first). Null = none up.</summary>
        public static Transform MapSubWindow()
        {
            var root = MapRoot();
            if (root == null) return null;
            foreach (var name in MapSubWindowsAll)
            {
                var t = ChildRendered(root, name);
                if (t != null) return t;
            }
            return null;
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

        // ---------- Character window (D18) ----------

        private static PlayMakerFSM _characterFsm;
        private static bool _characterChecked;

        /// <summary>Character window open: the Character UI Button FSM in "Open"
        /// (D18 — the game's own tutorial trigger polls exactly this dial).</summary>
        public static bool CharacterOpen()
        {
            if (!_characterChecked)
            {
                _characterFsm = FindFsm("Character UI Button");
                _characterChecked = true;
            }
            if (_characterFsm == null || _characterFsm.gameObject == null) return false;
            if (!_characterFsm.gameObject.activeInHierarchy) return false; // freeze guard
            // "Highlight 2" is the open-window HOVER state (sync pass F3: a
            // mouse resting on the top-bar button must not collapse the mode).
            string state = _characterFsm.ActiveStateName;
            return state == "Open" || state == "Highlight 2";
        }

        /// <summary>The shared Upgrade Confirm Window renders (alpha idiom) —
        /// forced-focus dialog; the character table stands down under it.</summary>
        public static bool CharacterConfirmUp()
        {
            var screen = GameObject.Find("Letterbox Canvas/Character Screen");
            var window = screen != null
                ? screen.transform.Find("Upgrade Confirm Window") : null;
            if (window == null || !window.gameObject.activeInHierarchy) return false;
            return Util.RenderedUp(window);
        }

        /// <summary>Character close: confirm window first (its own Back), else
        /// the lifecycle FSM's toggle event (D18: Back and the toggle both
        /// route Open→Close while the window is open).</summary>
        public static void CharacterBack()
        {
            var screen = GameObject.Find("Letterbox Canvas/Character Screen");
            var window = screen != null
                ? screen.transform.Find("Upgrade Confirm Window") : null;
            if (window != null && window.gameObject.activeInHierarchy
                && Util.RenderedUp(window))
            {
                var fsm = window.GetComponent<PlayMakerFSM>();
                if (fsm != null) { fsm.SendEvent("Back"); return; }
            }
            if (!_characterChecked)
            {
                _characterFsm = FindFsm("Character UI Button");
                _characterChecked = true;
            }
            if (_characterFsm != null) _characterFsm.SendEvent("Open");
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
            _characterFsm = null;
            _characterChecked = false;
            _mainMenuChecked = _pauseChecked = _mapChecked = _shipChecked = _cycleChecked = false;
            _focusFsm = null;
            _focusChecked = false;
            _inventoryFsm = null;
            _inventoryChecked = false;
        }

        // ---------- Inventory strip (D6, 2026-08-02) ----------

        private static PlayMakerFSM _inventoryFsm;
        private static bool _inventoryChecked;

        /// <summary>The strip's modality root (D6 (f)): the FSM on Bottom UI/
        /// Inventory whose resting states are Item 2 (mouse) / Item 3 (pad idle) /
        /// Item 5 (gamepad browse). Signature-verified — "Inventory" is not a
        /// unique GO name; the Item 3/Item 5 state pair is the mechanism class.</summary>
        public static PlayMakerFSM InventoryRootFsm()
        {
            if (!_inventoryChecked)
            {
                foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.gameObject == null) continue;
                    if (!fsm.gameObject.scene.IsValid()) continue;
                    if (fsm.gameObject.name != "Inventory") continue;
                    if (!HasState(fsm, "Item 5") || !HasState(fsm, "Item 3")) continue;
                    _inventoryFsm = fsm;
                    break;
                }
                _inventoryChecked = true;
            }
            return _inventoryFsm != null && _inventoryFsm.gameObject != null
                ? _inventoryFsm : null;
        }

        private static bool HasState(PlayMakerFSM fsm, string name)
        {
            var states = fsm.FsmStates;
            if (states == null) return false;
            foreach (var s in states)
                if (s.Name == name) return true;
            return false;
        }

        /// <summary>Browse mode: the root FSM in Item 5 — the game's OWN gamepad
        /// inventory-browse dial (D6 (f)). The char window and map park the root
        /// via Deactivate, so this is false wherever the strip isn't usable.</summary>
        public static bool InventoryBrowse()
        {
            var fsm = InventoryRootFsm();
            return fsm != null && fsm.gameObject.activeInHierarchy
                && fsm.ActiveStateName == "Item 5";
        }

        /// <summary>Enter/exit the game's own browse mode with the SAME events
        /// the native Rewired Inventory Toggle sends (events are inputs — the
        /// MapBack precedent). Only Item 3 carries the Activate transition; a
        /// press while the root idles in mouse mode (Item 2) would silently
        /// drop (sync pass 2026-08-02 F4), so that case arms a short pending
        /// send drained by InventoryPendingTick once the root's own per-frame
        /// gamepad hop lands in Item 3 — a retry window, not a speech delay.</summary>
        public static void InventoryToggle()
        {
            var fsm = InventoryRootFsm();
            if (fsm == null || !fsm.gameObject.activeInHierarchy)
            {
                Plugin.Log.LogInfo("[Game] InventoryToggle: no live strip root — ignored");
                return;
            }
            string state = fsm.ActiveStateName;
            if (state == "Item 5") { fsm.SendEvent("Deactivate"); return; }
            if (state == "Item 3") { fsm.SendEvent("Activate"); return; }
            EnsureGamepadMode();
            _inventoryActivateUntil = Time.unscaledTime + 0.5f;
        }

        private static float _inventoryActivateUntil = -1f;

        /// <summary>Drains a pending browse-mode entry (the Item 2 case) the
        /// frame the root reaches Item 3; expires loudly.</summary>
        public static void InventoryPendingTick()
        {
            if (_inventoryActivateUntil < 0f) return;
            var fsm = InventoryRootFsm();
            if (fsm == null || !fsm.gameObject.activeInHierarchy)
            { _inventoryActivateUntil = -1f; return; }
            if (fsm.ActiveStateName == "Item 3")
            {
                _inventoryActivateUntil = -1f;
                fsm.SendEvent("Activate");
                return;
            }
            if (Time.unscaledTime > _inventoryActivateUntil)
            {
                _inventoryActivateUntil = -1f;
                Plugin.Log.LogWarning(
                    "[Game] InventoryToggle: pending Activate expired — root never left "
                    + fsm.ActiveStateName + " (capture)");
            }
        }

        // ---------- Camera wire (D17 2026-08-01 — the decoded transfer function;
        // supersedes the live-sweep closed-loop stepping drive, which is DELETED).
        // The "wire" is a translation at Z/Y hubs and a gimbal rotation at two:
        // Focus Z / Focus Y are FSM-local scroll accumulators, X Rot Global IS
        // the gimbal accumulator; the FSM's own FloatSmoothDamp (0.2 s) glides
        // the Damped twin and applies it to the transform. The follow writes ONE
        // accumulator, ONCE, and never touches Damped anything, the read-only
        // Focus Z/Y Global mirrors, or the player's FreeLook rotation (decoded
        // independent — closed writer set; scrolling recenters rotation only on
        // native INPUT, so mod writes leave the player's yaw alone). ----------

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

        // Baked rig constants (D17, raw level2 transforms). S0 = the selector
        // origin (Focus world position at park); P = the Focus Gimbal pivot.
        private const float WireS0Y = 4813.777f;
        private const float WireS0Z = -18.667f;
        private const float GimbalPivotY = -3623f;
        private const float GimbalPivotZ = -4244f;
        private const float GimbalArmAngle = 143.397f;
        // Beyond this clamp distance the marker is authored out of the wire's
        // band (D17 anomaly classes 2/3): pan to the edge, then the selector
        // log is the tripwire.
        private const float OutOfBandSlack = 25f;

        /// <summary>Single-write camera pan to a marker's live world position
        /// (D17 mod recipe). Picks the wire by the Focus FSM's own active state
        /// (the mode truth), computes the closed-form target, clamps to the
        /// scene's authored band, writes the ONE live accumulator. Every failed
        /// assumption refuses loudly instead of mis-panning. Returns false when
        /// nothing was written; outOfBand = target clamped to the band edge;
        /// written = the clamped value (the settle-verify promise).</summary>
        public static bool CameraPanTo(Vector3 marker, out bool outOfBand, out float written)
        {
            outOfBand = false;
            written = 0f;
            var fsm = FocusFsm();
            if (fsm == null)
            {
                LogOnceGame("[Game] pan: no Focus FSM in scene — follow silent");
                return false;
            }

            // Patch guard (D17 recipe 4): the gimbal pivot is live-readable —
            // a game update that moves the rig must refuse, not mis-pan.
            var gimbal = fsm.transform.parent;
            if (gimbal == null
                || Mathf.Abs(gimbal.localPosition.y - GimbalPivotY) > 1f
                || Mathf.Abs(gimbal.localPosition.z - GimbalPivotZ) > 1f)
            {
                LogOnceGame("[Game] pan REFUSED: Focus Gimbal off the baked rig "
                    + "constants — re-run decode D17");
                return false;
            }

            // Mode truth = the FSM's own scroll state. Disabled 4/5 (map open,
            // orbital handoff) absorbs writes on re-entry (GetPosition re-seeds)
            // — defer; the next row change writes again.
            string state = fsm.ActiveStateName;
            if (state != "Z SCROLL" && state != "Y SCROLL" && state != "Gimbal Scroll")
            {
                Plugin.Log.LogInfo("[Game] pan deferred: Focus FSM in \"" + state + "\"");
                return false;
            }

            var min = GlobalFloat("Min Rotation");
            var max = GlobalFloat("Max Rotation");
            if (min == null || max == null || min.Value >= max.Value)
            {
                LogOnceGame("[Game] pan: fixed-view scene (Min >= Max) — never write");
                return false;
            }

            // Consistency check only (D17 recipe 2) — the state already ruled.
            var scrollMode = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmInt("Scroll Mode");
            int expected = state == "Z SCROLL" ? 1 : state == "Y SCROLL" ? 2 : 3;
            if (scrollMode != null && scrollMode.Value != expected)
                LogOnceGame("[Game] pan: Scroll Mode global " + scrollMode.Value
                    + " disagrees with FSM state \"" + state + "\" — capture");

            float target;
            if (state == "Z SCROLL")
                target = marker.z - WireS0Z;
            else if (state == "Y SCROLL")
                target = ((marker.y - WireS0Y) - 0.5f * (marker.z - WireS0Z)) / 1.25f;
            else
            {
                // Gimbal: ray from the live pivot through the marker (body is
                // parked at origin in this mode, so world == local checked above).
                var p = gimbal.position;
                target = GimbalArmAngle
                    - Mathf.Atan2(marker.y - p.y, marker.z - p.z) * Mathf.Rad2Deg;
            }

            float clamped = Mathf.Clamp(target, min.Value, max.Value);
            outOfBand = Mathf.Abs(clamped - target) > OutOfBandSlack;
            written = clamped;

            if (state == "Gimbal Scroll")
            {
                var accumulator = GlobalFloat("X Rot Global");
                if (accumulator == null)
                {
                    LogOnceGame("[Game] pan: no X Rot Global — capture");
                    return false;
                }
                accumulator.Value = clamped;
            }
            else
            {
                var accumulator = fsm.FsmVariables.GetFsmFloat(
                    state == "Z SCROLL" ? "Focus Z" : "Focus Y");
                if (accumulator == null)
                {
                    LogOnceGame("[Game] pan: Focus FSM lacks its own accumulator — capture");
                    return false;
                }
                accumulator.Value = clamped;
            }
            return true;
        }

        private static HutongGames.PlayMaker.FsmFloat GlobalFloat(string name)
            => HutongGames.PlayMaker.FsmVariables.GlobalVariables.GetFsmFloat(name);

        /// <summary>Settle verification for the single-write follow (ride V4
        /// recalibration: the SELECTOR was the wrong judge — lateral scatter
        /// makes it hold a neighbor even at a node's exact optimum, D17 live
        /// correction 4). The wire itself is the promise the transfer function
        /// makes: true when the damped twin has converged onto the written
        /// value (mode-aware tolerance — degrees are tighter than units).
        /// Missing rig/state reads true: nothing to judge, not a fault.</summary>
        public static bool WireSettled(float written, out float actual)
        {
            actual = written;
            var fsm = FocusFsm();
            if (fsm == null) return true;
            string state = fsm.ActiveStateName;
            string damped = state == "Z SCROLL" ? "Damped Z"
                : state == "Y SCROLL" ? "Damped Y"
                : state == "Gimbal Scroll" ? "Damped X" : null;
            if (damped == null) return true;
            var v = fsm.FsmVariables.GetFsmFloat(damped);
            if (v == null) return true;
            actual = v.Value;
            float tolerance = state == "Gimbal Scroll" ? 0.5f : 5f;
            return Mathf.Abs(actual - written) <= tolerance;
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
                // Pip-class exclusion (ride V4, 2026-08-01: the Contract Board's
                // "Complete a Contract" drive pips defeat BOTH factors — they
                // carry ClockValue as consumers and draw their ring with a
                // UICircle; the bare-value log caught the false "clock 0"). An
                // FSM whose state set is the pip mechanism is a pip, never a clock.
                if (HasStatePair(fsm, "PIP on", "PIP off")) continue;
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

        private static bool HasStatePair(PlayMakerFSM fsm, string a, string b)
        {
            var states = fsm.FsmStates;
            if (states == null) return false;
            bool hasA = false, hasB = false;
            foreach (var s in states)
            {
                if (s.Name == a) hasA = true;
                else if (s.Name == b) hasB = true;
            }
            return hasA && hasB;
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
