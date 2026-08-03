using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sleeptalker.Scaffold
{
    /// <summary>Keyboard navigation over the game's interactable uGUI elements.
    /// Selection goes through EventSystem so the FocusPatch announces it.</summary>
    internal static class Navigator
    {
        public static void Select(GameObject go)
        {
            Diag.Note("Nav", "select " + (go != null ? go.name : "(null)"));
            SpeechService.FlushQueue();
            NavSignals.UserNavigated();
            NavSignals.ClearCooldown(go);
            var es = EventSystem.current;
            if (es == null) return;
            if (es.currentSelectedGameObject == go)
            {
                // Re-announce even if unchanged.
                SpeechService.Say(ElementDescriber.Element(go, false), Priority.Immediate, "focus");
                return;
            }
            es.SetSelectedGameObject(go);
        }

        public static GameObject Current()
        {
            return EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        }

        /// <summary>Move selection the way a controller dpad press does: a uGUI move event
        /// through the game's own navigation graph. Never picks targets itself.</summary>
        public static void Move(MoveDirection direction)
        {
            SpeechService.FlushSource("focus");
            NavSignals.UserNavigated();
            var es = EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            if (current == null || !current.activeInHierarchy)
            {
                // Nothing focused: adopt the first interactable selectable so the
                // navigation graph has a starting point.
                foreach (var s in Object.FindObjectsOfType<Selectable>())
                {
                    if (s.IsInteractable() && s.gameObject.activeInHierarchy)
                    {
                        es.SetSelectedGameObject(s.gameObject);
                        return;
                    }
                }
                // C4: scene-start auto-reads spoke their empty result at frame 1
                // (2 of 3 cold boots) — an empty world is boot noise, not news.
                if (Time.unscaledTime < Timing.BootQuietWindow)
                    Plugin.Log.LogInfo("[Nav] nothing to focus (boot window, silent).");
                else
                    SpeechService.Say("Nothing to focus.", Priority.Immediate, "nav");
                return;
            }
            var move = new AxisEventData(es)
            {
                moveDir = direction,
                moveVector = direction switch
                {
                    MoveDirection.Up => Vector2.up,
                    MoveDirection.Down => Vector2.down,
                    MoveDirection.Left => Vector2.left,
                    _ => Vector2.right,
                },
            };
            ExecuteEvents.Execute(current, move, ExecuteEvents.moveHandler);
            var landed = es.currentSelectedGameObject;
            Diag.Note("Nav", "move " + direction + " from " + current.name
                + (landed == current ? " (dead end)" : " to " + (landed != null ? landed.name : "(none)")));

            // Dead end (or sole focusable element): repeat the element bare — the repeat
            // itself is the dead-end signal, and an arrow press must never be silent.
            if (es.currentSelectedGameObject == current)
                SpeechService.Say(ElementDescriber.Element(current, false),
                    Priority.Immediate, "focus");
        }

        // Activation echo (live finding, CS2 first ride): an in-place toggle changes
        // its own rendered label but fires no focus event — silent flip. After any
        // activation, re-read the element once it settles; if it is still focused
        // and reads differently, speak the new state. Unchanged labels stay silent.
        private const float EchoDefer = 0.3f;
        private static GameObject _echoGo;
        private static string _echoBefore;
        private static float _echoAt = -1f;

        public static void Tick()
        {
            if (_echoAt < 0 || Time.unscaledTime < _echoAt) return;
            _echoAt = -1f;
            var go = _echoGo;
            _echoGo = null;
            if (go == null || !go.activeInHierarchy) return;
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject != go) return;
            string after = ElementDescriber.Element(go, false);
            if (!string.IsNullOrEmpty(after) && after != _echoBefore)
                SpeechService.Say(after, Priority.Immediate, "nav");
        }

        private static float _lastActivate = -1f;

        /// <summary>Activate the focused element. The game's keyboard map only navigates —
        /// it never submits — so Enter activation is the mod's job. Rapid repeats are
        /// debounced: scripted UI (tutorial chains) can be mid-transition right after an
        /// activation, and a second click there breaks the script.</summary>
        public static void ActivateCurrent()
        {
            var go = Current();
            if (go == null)
            {
                SpeechService.Say("Nothing focused.", Priority.Immediate, "nav");
                return;
            }
            if (Time.unscaledTime - _lastActivate < Timing.ActivateDebounce) return;
            _lastActivate = Time.unscaledTime;
            Click(go);
        }

        public static void Click(GameObject go) => Click(go, false);

        /// <summary>Pointer-path activation — the sequence a real mouse press sends.
        /// For machinery that listens to pointer events (EventTrigger-fed FSMs)
        /// rather than uGUI submit: the tutorial CONTINUE capture (f4641) showed the
        /// Button consuming submit while the popup's dismissal never saw it. Still
        /// exactly one dispatch path per press.</summary>
        public static void PointerClick(GameObject go) => Click(go, true);

        private static void Click(GameObject go, bool pointerPath)
        {
            Diag.Note("Nav", (pointerPath ? "pointer-click " : "click ") + (go != null ? go.name : "(null)"));
            // A disabled Selectable swallows clicks silently — say so instead
            // (the game gates buttons this way, e.g. End Cycle during the intro).
            // Component-disabled counts too: ExecuteEvents skips a .enabled=false
            // Button, and a sibling EventTrigger then reports the pointer path
            // "activated" without the onClick ever firing (skill-rung capture
            // 2026-08-03).
            var selectable = go.GetComponent<Selectable>();
            if (selectable != null
                && (!selectable.isActiveAndEnabled || !selectable.IsInteractable()))
            {
                SpeechService.Say(ElementDescriber.Element(go, false) + ".", Priority.Immediate, "nav");
                return;
            }

            // Exactly ONE activation path, like Unity's own input module (submit for
            // keyboard/gamepad, pointer click as fallback). Firing both invokes a
            // Button's onClick twice per press — double-dispatching scripted events
            // (the tutorial-chain hangs, sessions 2 through 6).
            string before = ElementDescriber.Element(go, false);
            var es = EventSystem.current;
            bool activated;
            if (pointerPath)
            {
                activated = PointerSequence(go, es);
            }
            else
            {
                activated = ExecuteEvents.Execute(go, new BaseEventData(es), ExecuteEvents.submitHandler);
                if (!activated) activated = PointerSequence(go, es);
            }
            if (!activated)
            {
                SpeechService.Say("Not activatable.", Priority.Immediate, "nav");
                return;
            }
            _echoGo = go;
            _echoBefore = before;
            _echoAt = Time.unscaledTime + EchoDefer;
        }

        /// <summary>Hover dispatch — pointerEnter/Exit only, no click. The highlight
        /// path: camera-follow machinery rides hover, not selection (owner ruling
        /// 2026-07-26: selection is the authored camera move; highlight is the
        /// follow method). Mirrors the dev bridge's /hover.</summary>
        public static void Hover(GameObject go, bool exit)
        {
            if (go == null) return;
            var ped = new PointerEventData(EventSystem.current);
            if (exit) ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerExitHandler);
            else ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerEnterHandler);
        }

        private static bool PointerSequence(GameObject go, EventSystem es)
        {
            var ped = new PointerEventData(es);
            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerUpHandler);
            return ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
        }
    }
}
