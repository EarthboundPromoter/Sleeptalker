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

            // Action view: the location table owns the keys — the D4 stacked grid
            // (its Active() and the zone table's are the same dial, opposite signs).
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

            if (Input.GetKeyDown(KeyCode.Backspace)) { ResolveCancel(); return; }
            if (Input.GetKeyDown(KeyCode.Z)) { SpeechService.RepeatLast(); return; }
            if (Input.GetKeyDown(KeyCode.F3)) { Diag.IncidentDump("F3"); return; }
        }

        /// <summary>Backspace = the designed cancel, resolved per mode (CS1
        /// ResolveCancel idiom; the full ladder arrives with the ModeModel). First
        /// rung, ride V1 finding: the action view had no keyboard way out. The
        /// game's own out is the Leave button — a GameObject-typed global holds it
        /// (port-audit §7b) — so Backspace clicks exactly what a pointer would.</summary>
        private static void ResolveCancel()
        {
            // Above-the-table floors first: pause and dice allocation own their own
            // cancel semantics (pause = the game's own Esc path; die retraction =
            // the designed Back, decode D11 pending). Never fire Leave under them.
            if (GameQueries.Paused() || GameQueries.DiceAllocationLive())
            {
                Plugin.Log.LogInfo("[Input] Backspace under pause/allocation: no rung yet");
                return;
            }
            var actionView = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmBool("Action View?");
            if (actionView != null && actionView.Value)
            {
                var leave = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                    .GetFsmGameObject("Leave Button");
                if (leave != null && leave.Value != null && leave.Value.activeInHierarchy)
                {
                    Navigator.Click(leave.Value);
                    return;
                }
                Plugin.Log.LogWarning(
                    "[Input] action view up but no live Leave Button global — cancel path capture needed");
                SpeechService.Say(Lex.T("cancel.none"), Priority.Immediate, "nav");
                return;
            }
            // Other modes: no cancel rung yet (skeleton) — log, stay silent.
            Plugin.Log.LogInfo("[Input] Backspace: no cancel path in this mode yet");
        }

        private static readonly KeyCode[] ModKeys =
        {
            // Space was missing from this gate until ride V1 — the table grammar's
            // Space (full row / detail) never reached the engine. Keep in sync with
            // every key any surface consumes.
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space, KeyCode.Backspace,
            KeyCode.Z, KeyCode.F3,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        };

        private static bool IsModKeyDown()
        {
            foreach (var key in ModKeys)
                if (Input.GetKeyDown(key)) return true;
            return false;
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
