using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Keyboard dispatch, skeleton stage. The CS1 input contract carries
    /// whole: the game has NO keyboard UI input — the mod supplies it, but always
    /// routed through the game's own machinery (move events on the current selection,
    /// submit/click on activation; never picking targets from sorted lists). Keys:
    /// arrows navigate, Enter activates, 1–9 pick dialogue responses, Z repeats,
    /// F3 dumps diagnostics. Everything else arrives with its surface.</summary>
    internal sealed class InputManager
    {
        public void Tick()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Diag.CaptureKeys(shift);

            // A mouse click claims mouse mode for a sighted co-pilot.
            if (Input.GetMouseButtonDown(0)) GameQueries.EnsureMouseMode();

            PauseAutosaveWatch();

            if (!Input.anyKeyDown) return;
            if (!IsModKeyDown()) return;

            // Any mod key: the scene is settled (boot-sweep silence ends) and the
            // UI must be in gamepad mode for the keyboard flow to work.
            FocusPatch.MarkSettled();
            GameQueries.EnsureGamepadMode();

            // The F1 key sheet owns the table grammar while open (owner
            // design 2026-08-03); a foreign key retires it silently and
            // falls through to its owner below.
            if (HelpTable.HandleKeys()) return;

            // --- Response menu: number picks + vertical remap over the horizontal
            //     graph (CS1 idiom; CS2 response layout verify-live). A tutorial
            //     popup takes focus OVER an open menu (live finding: Skill Check
            //     Tutorial) — while focus is inside the Tutorial System, the box
            //     owns the keys and the remap stands down. ---
            // Tutorial modal: the walkable buffer owns the arrows (Up/Down blocks,
            // Left/Right repeat); Enter always fires CONTINUE via the pointer path
            // (owner ruling — native submit is consumed by the Button while the
            // game's dismissal machinery never sees it, the f4641 trap).
            if (TutorialReader.Active())
            {
                if (Input.GetKeyDown(KeyCode.DownArrow)) { TutorialReader.Move(1); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow)) { TutorialReader.Move(-1); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow))
                { TutorialReader.Repeat(); return; }
                if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    && TutorialReader.Dismiss())
                    return;
            }

            // Unified dialogue column (owner design 2026-08-01, D19): Up from
            // the live conversation walks the transcript back (window scrolls
            // with it); Down returns to the frontier; Enter in history re-reads.
            // The frontier itself keeps the SHIPPED native grammar below.
            if (ConversationEvents.ConversationActive && !TutorialReader.Active())
            {
                if (Input.GetKeyDown(KeyCode.UpArrow) && DialogueColumn.InHistory
                    == false && !DialogueState.MenuOpen && DialogueColumn.Up()) return;
                if (DialogueColumn.InHistory)
                {
                    if (Input.GetKeyDown(KeyCode.UpArrow)) { DialogueColumn.Up(); return; }
                    if (Input.GetKeyDown(KeyCode.DownArrow)) { DialogueColumn.Down(); return; }
                    if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                        && DialogueColumn.EnterKey()) return;
                }
            }

            if (DialogueState.MenuOpen && !TutorialReader.Active())
            {
                for (int i = 0; i < 9 && i < DialogueState.CurrentResponses.Count; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { PickResponse(i); return; }
                }
                // Up at the TOP of the choice list crosses into history (the
                // no-barrier rule): the native remap otherwise owns the axis.
                if (Input.GetKeyDown(KeyCode.DownArrow)) { Navigator.Move(MoveDirection.Right); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    var before = Navigator.Current();
                    Navigator.Move(MoveDirection.Left);
                    if (Navigator.Current() == before) DialogueColumn.Up();
                    return;
                }
            }

            // Class select (owner design): Up/Down walk the class card rows with a
            // confirm-button handoff at the end; Left/Right drive the carousel FSM's
            // own swap events; Enter inside the table jumps to confirm first.
            if (ClassSelect.Active())
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { ClassSelect.Swap(-1); return; }
                if (Input.GetKeyDown(KeyCode.RightArrow)) { ClassSelect.Swap(1); return; }
                if (Input.GetKeyDown(KeyCode.DownArrow)) { ClassSelect.Browse(1); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow)) { ClassSelect.Browse(-1); return; }
                if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    && ClassSelect.EnterKey())
                    return;
            }

            // Pause options review (CS1 OptionsReview port, D7): while the
            // Options Menu panel renders, the review owns arrows + Enter —
            // Up/Down rows, Left/Right mutate natively, Enter presses Back.
            // Esc stays the game's own back-out, one level at a time.
            if (OptionsReview.IsActive())
            {
                if (Input.GetKeyDown(KeyCode.DownArrow)) { OptionsReview.Review(1); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow)) { OptionsReview.Review(-1); return; }
                if (Input.GetKeyDown(KeyCode.RightArrow)) { OptionsReview.Adjust(1); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { OptionsReview.Adjust(-1); return; }
                if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    && OptionsReview.Activate())
                    return;
            }

            // Guide reader (owner design 2026-08-01, D7 (d)): two-pane node
            // graph — topics left, live paragraph box right; Enter = native
            // radio-group click, cursor into the fresh page at the top.
            if (GuideReader.IsActive())
            {
                if (Input.GetKeyDown(KeyCode.DownArrow)) { GuideReader.Move(1); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow)) { GuideReader.Move(-1); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { GuideReader.LeftKey(); return; }
                if (Input.GetKeyDown(KeyCode.RightArrow)) { GuideReader.RightKey(); return; }
                if (Input.GetKeyDown(KeyCode.Space)) { GuideReader.SpaceKey(); return; }
                if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    && GuideReader.Activate())
                    return;
            }

            // The top-bar V table: an excursion above the zone/location tables
            // (owner ruling 2026-07-31 — the whole bar walkable, buttons included).
            if (TopBarTable.HandleKeys()) return;

            // Drive log: J toggles (the Drive Log Button FSM's own Open, both
            // directions); while open, the journal table owns the keys.
            if (Input.GetKeyDown(KeyCode.J))
            {
                // J joins the inventory swap set (owner ruling 2026-08-02,
                // ride finding: the drives screen was unreachable from browse).
                var mode = SwapOutOfInventory(ModeModel.Current());
                if (mode == Mode.DriveLog || mode == Mode.Station
                    || mode == Mode.RigRooms || mode == Mode.ActionView)
                { JournalTable.Toggle(); return; }
            }
            if (ModeModel.Current() == Mode.DriveLog && JournalTable.HandleKeys()) return;

            // Inventory strip: I toggles the game's OWN browse mode (the native
            // Rewired Inventory Toggle's events, D6); while browsing, the
            // inventory table owns the keys behind the fence.
            if (Input.GetKeyDown(KeyCode.I))
            {
                var invMode = ModeModel.Current();
                if (invMode == Mode.Inventory || invMode == Mode.Station
                    || invMode == Mode.RigRooms || invMode == Mode.ActionView)
                { GameQueries.InventoryToggle(); return; }
            }
            if (InventoryTable.Active() && InventoryTable.HandleKeys()) return;

            // Map mode: the map table owns arrows/Enter over the marker planes
            // (spec: before native fallthrough). Native forced-focus sub-windows
            // (Travel Confirm, Crew, Leave Contract, blockers) stand it down via
            // the Active gate — their focus reads carry them (D8).
            if (MapTable.Active() && MapTable.HandleKeys()) return;

            // Character window: the skills + push table owns the keys; the
            // shared Upgrade Confirm Window stands it down (native focus).
            if (CharacterTable.Active() && CharacterTable.HandleKeys()) return;

            // Action view: the location table owns the keys — the D4 stacked grid
            // (mode-gated by the ModeModel).
            if (LocationTable.Active() && LocationTable.HandleKeys()) return;

            // Open-station mode: the zone table owns arrows and Enter — the atlas
            // nav idiom (owner design 2026-07-26). WASD stays the game's camera.
            if (ZoneTable.Active() && ZoneTable.HandleKeys()) return;

            // Pause selection anchor (ride V4 state bug 2026-08-01: arrows in
            // the first beat after Esc walked the STALE station selection
            // beneath the fresh pause overlay — the game's own RESUME reselect
            // lands a beat later). In Pause mode, a move or Enter whose
            // selection sits outside the Pause Canvas re-anchors onto the menu
            // (the game's own reselect idiom) instead of driving the world
            // underneath. One press = the anchor; the next press navigates.
            bool navKey = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)
                || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (navKey && ModeModel.Current() == Mode.Pause)
            {
                var selected = Navigator.Current();
                if (selected == null || !Util.HasAncestor(selected, "Pause Canvas"))
                {
                    var canvas = GameObject.Find("PAUSE/Pause Canvas");
                    var anchor = canvas != null
                        ? canvas.GetComponentInChildren<Selectable>(false) : null;
                    if (anchor != null)
                    {
                        Navigator.Select(anchor.gameObject);
                        return;
                    }
                }
            }

            // Dice cross-row nav (owner ruling 2026-08-02): native uGUI links
            // only column-aligned dice — Down from player dice 3-5 went
            // nowhere. The remap drops any player die onto the crew rows
            // (1→1, 2-5→2), keeps crew⇄crew aligned, and returns aligned.
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (ModeModel.Current() == Mode.DiceAllocation
                    && DiceFlow.CrossRowNav(-1)) return;
                Navigator.Move(MoveDirection.Up); return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (ModeModel.Current() == Mode.DiceAllocation
                    && DiceFlow.CrossRowNav(1)) return;
                Navigator.Move(MoveDirection.Down); return;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { Navigator.Move(MoveDirection.Left); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { Navigator.Move(MoveDirection.Right); return; }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                // A keyboard-armed push confirm owns Enter (sync pass HIGH-1:
                // the V-row commit exits the table before arming, so the
                // second press needs this rung; P remains the twin).
                if (PushFlow.ModArmed()) { PushFlow.Key(); return; }
                Navigator.ActivateCurrent();
                return;
            }

            // Direct keys (owner ruling 2026-07-31): M = map, G = rig toggle —
            // the same native clicks the V-table rows fire, scoped to the floors
            // where the buttons render (dead elsewhere, KeyScope idiom).
            if (Input.GetKeyDown(KeyCode.M)) { MapKey(); return; }
            if (Input.GetKeyDown(KeyCode.G)) { RigKey(); return; }
            if (Input.GetKeyDown(KeyCode.U)) { CharacterKey(); return; }
            if (Input.GetKeyDown(KeyCode.P)) { PushKey(); return; }

            if (Input.GetKeyDown(KeyCode.Backspace)) { ResolveCancel(); return; }
            if (Input.GetKeyDown(KeyCode.Z)) { SpeechService.RepeatLast(); return; }
            // N = census replay (CS1 key convention, ported with the census).
            if (Input.GetKeyDown(KeyCode.N)) { StationCensus.SpeakLast(); return; }
            if (Input.GetKeyDown(KeyCode.F1)) { HelpTable.Toggle(); return; }
            if (Input.GetKeyDown(KeyCode.F3)) { Diag.IncidentDump("F3"); return; }
        }

        /// <summary>Backspace = the designed cancel, resolved by the ModeModel
        /// (CS1 ResolveCancel shape; every rung a decoded designed input —
        /// D8/D9/D11). A press is never swallowed: rungs that find nothing fall
        /// to the next mode's designed out.</summary>
        private static void ResolveCancel()
        {
            // An armed push confirm is the topmost transient on any floor —
            // Backspace cancels it through the game's own Mouseoff (the
            // disarm watch speaks the close).
            if (PushFlow.CancelArm()) return;
            switch (ModeModel.Current())
            {
                case Mode.Pause:
                    // The pause menu's own Back/Esc machinery is native.
                    Plugin.Log.LogInfo("[Input] Backspace under pause: Esc path is native");
                    return;

                case Mode.Map:
                    // The map's own close is the Back event (D8: Back Button FSM
                    // fires it off Rewired Back; same event, same target).
                    GameQueries.MapBack();
                    return;

                case Mode.Character:
                    // Confirm window first, else the lifecycle toggle (D18).
                    GameQueries.CharacterBack();
                    return;

                case Mode.DriveLog:
                    JournalTable.Toggle(); // the button FSM's Open, both directions
                    return;

                case Mode.Inventory:
                    // The same Deactivate the native toggle and watchdog send.
                    GameQueries.InventoryToggle();
                    return;

                case Mode.DiceAllocation:
                case Mode.ActionView:
                    // Slot rungs FIRST on both floors (ride finding: an ITEM-cost
                    // slot rests in Slot Item without engaging the dice systems —
                    // mode stays ActionView, Leave is deactivated by the resting
                    // engagement, and only the slot's own Reset frees it. D11 §4).
                    if (DiceFlow.CancelRung()) return;
                    var leave = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                        .GetFsmGameObject("Leave Button");
                    if (leave != null && leave.Value != null && leave.Value.activeInHierarchy)
                    {
                        Navigator.Click(leave.Value);
                        return;
                    }
                    Plugin.Log.LogWarning(
                        "[Input] action view up but no live Leave Button global — capture needed");
                    SpeechService.Say(Lex.T("cancel.none"), Priority.Immediate, "nav");
                    return;

                case Mode.RigRooms:
                    // The designed rig exit: the Ship toggle (Idle Ship side, D9).
                    var ship = GameQueries.ShipToggleButton();
                    if (ship != null) { Navigator.Click(ship); return; }
                    Plugin.Log.LogWarning("[Input] rig side but no Ship toggle button — capture needed");
                    SpeechService.Say(Lex.T("cancel.none"), Priority.Immediate, "nav");
                    return;

                default:
                    Plugin.Log.LogInfo("[Input] Backspace: no cancel rung in mode "
                        + ModeModel.Name());
                    return;
            }
        }

        private static readonly KeyCode[] ModKeys =
        {
            // Space was missing from this gate until ride V1 — the table grammar's
            // Space (full row / detail) never reached the engine. Keep in sync with
            // every key any surface consumes.
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space, KeyCode.Backspace,
            KeyCode.V, KeyCode.M, KeyCode.G, KeyCode.J, KeyCode.U, KeyCode.I, KeyCode.P,
            KeyCode.N, KeyCode.Slash,
            KeyCode.Z, KeyCode.F1, KeyCode.F3,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        };

        private static bool IsModKeyDown()
        {
            foreach (var key in ModKeys)
                if (Input.GetKeyDown(key)) return true;
            return false;
        }

        private static bool _wasPaused;
        private static bool _autosaveLineMissLogged;

        /// <summary>Esc renders the pause menu's "Time Since Last Autosave"
        /// status line (D7 (a): label TMP + live value in its child — the
        /// game's "SInce" typo is wart-registered) — spoken automatically on
        /// pause entry (owner ruling 2026-08-02). Pre-intro it renders the
        /// complete-the-intro notice instead; both speak as drawn.</summary>
        private static void PauseAutosaveWatch()
        {
            bool paused = GameQueries.Paused();
            if (paused == _wasPaused) return;
            _wasPaused = paused;
            if (!paused) return;
            var go = GameObject.Find("PAUSE/Pause Canvas/Time Since Last Autosave");
            if (go == null || !go.activeInHierarchy)
            {
                if (!_autosaveLineMissLogged)
                {
                    _autosaveLineMissLogged = true;
                    Plugin.Log.LogWarning("[Pause] autosave status line not found — capture");
                }
                return;
            }
            var sb = new System.Text.StringBuilder();
            foreach (var tmp in go.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(text);
            }
            if (sb.Length > 0)
                SpeechService.Say(sb.Append('.').ToString(), Priority.Queued, "pause");
        }

        /// <summary>Surface swap out of inventory browse (owner ruling
        /// 2026-08-02): M/G/U auto-close the strip (the native Deactivate) and
        /// fall through to their normal action — the underlying floor is a
        /// gameplay floor by construction (the strip only browses there).</summary>
        private static Mode SwapOutOfInventory(Mode mode)
        {
            if (mode != Mode.Inventory) return mode;
            GameQueries.InventoryToggle();
            return Mode.Station; // stand-in for "a gameplay floor" in the scoping checks
        }

        private static void MapKey()
        {
            var mode = SwapOutOfInventory(ModeModel.Current());
            if (mode == Mode.Map) { GameQueries.MapBack(); return; } // toggle out
            if (mode != Mode.Station && mode != Mode.RigRooms && mode != Mode.ActionView)
                return; // dead out of scope
            var button = GameObject.Find(
                "Letterbox Canvas/Top UI/Ship and Map Buttons/Map UI/Button");
            if (button != null) { Navigator.Click(button); return; }
            Plugin.Log.LogWarning("[Input] M: no Map button on this floor");
            SpeechService.Say(Lex.T("map.unavailable"), Priority.Immediate, "nav");
        }

        /// <summary>U = the character/skills/upgrade window (owner key ruling,
        /// ride V5): the lifecycle FSM's own toggle event, both directions —
        /// same scoping as M (dead off the gameplay floors).</summary>
        private static void CharacterKey()
        {
            var mode = SwapOutOfInventory(ModeModel.Current());
            if (mode == Mode.Character) { GameQueries.CharacterBack(); return; }
            if (mode != Mode.Station && mode != Mode.RigRooms && mode != Mode.ActionView)
                return;
            GameQueries.CharacterBack(); // the toggle event opens from Idle too
        }

        /// <summary>G = the Ship toggle's native click WHERE THE GAME DRAWS THE
        /// BUTTON; everywhere else silent-dead (owner ruling 2026-08-02 — no
        /// unavailability announce; the log keeps the evidence). The toggle
        /// itself is the whole jump: the game swaps the UI side AND zooms the
        /// camera down to the rig locale, so the mod adds nothing on top.</summary>
        /// <summary>P = the push two-press (owner ruling 2026-08-02, the push
        /// vocabulary session — supersedes the raw-button-click provisional,
        /// which bypassed the game's confirm stage, D20 §g): PushFlow drives
        /// the Push System FSM's own event ladder — press 1 arms and speaks
        /// the rendered confirm box, press 2 fires; dead presses speak their
        /// reason from the state dial. The V-row PUSH cell's Enter is the
        /// same path.</summary>
        private static void PushKey()
        {
            var mode = SwapOutOfInventory(ModeModel.Current());
            if (mode != Mode.Station && mode != Mode.RigRooms && mode != Mode.ActionView)
                return;
            PushFlow.Key();
        }

        private static void RigKey()
        {
            var mode = SwapOutOfInventory(ModeModel.Current());
            if (mode != Mode.Station && mode != Mode.RigRooms) return; // dead out of scope
            var ship = GameQueries.ShipToggleButton();
            if (ship != null && ship.activeInHierarchy && Util.RenderedUp(ship.transform))
            { Navigator.Click(ship); return; }
            Plugin.Log.LogInfo("[Input] G: Ship toggle not drawn here — silent");
        }

        // F1 help is the invisible key table now (HelpTable, owner design
        // 2026-08-03) — the prose SpeakHelp read retired with it.

        /// <summary>CS1 idiom: response buttons are named "Response: " + rendered text.
        /// If CS2 names differ, the miss is logged and spoken — verify-live seam.</summary>
        private void PickResponse(int index)
        {
            string text = DialogueState.CurrentResponses[index];
            var buttonName = "Response: " + text;
            foreach (var b in Object.FindObjectsOfType<Button>())
            {
                if (b.gameObject.name == buttonName && b.IsInteractable())
                {
                    DialogueState.MenuOpen = false;
                    Navigator.Click(b.gameObject);
                    return;
                }
            }
            Plugin.Log.LogWarning("[Input] response button not found by name: " + buttonName);
            SpeechService.Say("Choice " + (index + 1) + " not clickable yet.", Priority.Immediate, "nav");
        }
    }
}
