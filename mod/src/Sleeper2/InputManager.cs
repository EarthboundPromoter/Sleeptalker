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

            if (!Input.anyKeyDown) return;
            if (!IsModKeyDown()) return;

            // Any mod key: the scene is settled (boot-sweep silence ends) and the
            // UI must be in gamepad mode for the keyboard flow to work.
            FocusPatch.MarkSettled();
            GameQueries.EnsureGamepadMode();

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

            if (DialogueState.MenuOpen && !TutorialReader.Active())
            {
                for (int i = 0; i < 9 && i < DialogueState.CurrentResponses.Count; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { PickResponse(i); return; }
                }
                if (Input.GetKeyDown(KeyCode.DownArrow)) { Navigator.Move(MoveDirection.Right); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow)) { Navigator.Move(MoveDirection.Left); return; }
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

            // The top-bar V table: an excursion above the zone/location tables
            // (owner ruling 2026-07-31 — the whole bar walkable, buttons included).
            if (TopBarTable.HandleKeys()) return;

            // Action view: the location table owns the keys — the D4 stacked grid
            // (mode-gated by the ModeModel).
            if (LocationTable.Active() && LocationTable.HandleKeys()) return;

            // Open-station mode: the zone table owns arrows and Enter — the atlas
            // nav idiom (owner design 2026-07-26). WASD stays the game's camera.
            if (ZoneTable.Active() && ZoneTable.HandleKeys()) return;

            if (Input.GetKeyDown(KeyCode.UpArrow)) { Navigator.Move(MoveDirection.Up); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { Navigator.Move(MoveDirection.Down); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { Navigator.Move(MoveDirection.Left); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { Navigator.Move(MoveDirection.Right); return; }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Navigator.ActivateCurrent();
                return;
            }

            // Direct keys (owner ruling 2026-07-31): M = map, G = rig toggle —
            // the same native clicks the V-table rows fire, scoped to the floors
            // where the buttons render (dead elsewhere, KeyScope idiom).
            if (Input.GetKeyDown(KeyCode.M)) { MapKey(); return; }
            if (Input.GetKeyDown(KeyCode.G)) { RigKey(); return; }

            if (Input.GetKeyDown(KeyCode.Backspace)) { ResolveCancel(); return; }
            if (Input.GetKeyDown(KeyCode.Z)) { SpeechService.RepeatLast(); return; }
            if (Input.GetKeyDown(KeyCode.F1)) { SpeakHelp(); return; }
            if (Input.GetKeyDown(KeyCode.F3)) { Diag.IncidentDump("F3"); return; }
        }

        /// <summary>Backspace = the designed cancel, resolved by the ModeModel
        /// (CS1 ResolveCancel shape; every rung a decoded designed input —
        /// D8/D9/D11). A press is never swallowed: rungs that find nothing fall
        /// to the next mode's designed out.</summary>
        private static void ResolveCancel()
        {
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
            KeyCode.V, KeyCode.M, KeyCode.G, KeyCode.Z, KeyCode.F1, KeyCode.F3,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        };

        private static bool IsModKeyDown()
        {
            foreach (var key in ModKeys)
                if (Input.GetKeyDown(key)) return true;
            return false;
        }

        private static void MapKey()
        {
            var mode = ModeModel.Current();
            if (mode == Mode.Map) { GameQueries.MapBack(); return; } // toggle out
            if (mode != Mode.Station && mode != Mode.RigRooms && mode != Mode.ActionView)
                return; // dead out of scope
            var button = GameObject.Find(
                "Letterbox Canvas/Top UI/Ship and Map Buttons/Map UI/Button");
            if (button != null) { Navigator.Click(button); return; }
            Plugin.Log.LogWarning("[Input] M: no Map button on this floor");
            SpeechService.Say(Lex.T("map.unavailable"), Priority.Immediate, "nav");
        }

        private static void RigKey()
        {
            var mode = ModeModel.Current();
            if (mode != Mode.Station && mode != Mode.RigRooms) return; // dead out of scope
            var ship = GameQueries.ShipToggleButton();
            if (ship != null) { Navigator.Click(ship); return; }
            Plugin.Log.LogWarning("[Input] G: no Ship toggle on this floor");
            SpeechService.Say(Lex.T("rig.unavailable"), Priority.Immediate, "nav");
        }

        /// <summary>F1 — contextual help (CS1 KeyScope idiom, minimal form): the
        /// current mode and the keys live on it. Grows into the full KeyScope
        /// table as surfaces accumulate.</summary>
        private static void SpeakHelp()
        {
            var mode = ModeModel.Current();
            string keys;
            switch (mode)
            {
                case Mode.Station:
                case Mode.RigRooms:
                case Mode.ActionView:
                    keys = Lex.T(TopBarTable.Entered ? "help.topbar" : "help.table");
                    break;
                case Mode.Tutorial: keys = Lex.T("help.tutorial"); break;
                case Mode.Conversation: keys = Lex.T("help.conversation"); break;
                case Mode.DiceAllocation: keys = Lex.T("help.dice"); break;
                case Mode.Map: keys = Lex.T("help.map"); break;
                case Mode.Pause: keys = Lex.T("help.native"); break;
                default: keys = Lex.T("help.native"); break;
            }
            SpeechService.Say(Lex.T("mode." + mode.ToString().ToLowerInvariant())
                + ". " + keys, Priority.Immediate, "query");
        }

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
