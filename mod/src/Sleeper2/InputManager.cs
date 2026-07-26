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
            //     graph (CS1 idiom; CS2 response layout verify-live) ---
            if (DialogueState.MenuOpen)
            {
                for (int i = 0; i < 9 && i < DialogueState.CurrentResponses.Count; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { PickResponse(i); return; }
                }
                if (Input.GetKeyDown(KeyCode.DownArrow)) { Navigator.Move(MoveDirection.Right); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow)) { Navigator.Move(MoveDirection.Left); return; }
            }

            if (Input.GetKeyDown(KeyCode.UpArrow)) { Navigator.Move(MoveDirection.Up); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { Navigator.Move(MoveDirection.Down); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { Navigator.Move(MoveDirection.Left); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { Navigator.Move(MoveDirection.Right); return; }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Navigator.ActivateCurrent();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Z)) { SpeechService.RepeatLast(); return; }
            if (Input.GetKeyDown(KeyCode.F3)) { Diag.IncidentDump("F3"); return; }
        }

        private static readonly KeyCode[] ModKeys =
        {
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Z, KeyCode.F3,
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
