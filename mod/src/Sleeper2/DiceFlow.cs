using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The dice-allocation announce layer (decode D11, 2026-07-31 —
    /// docs/decodes/D11-dice-delta.md). The game's own flow is keyboard-drivable
    /// end to end (Submit opens the picker, arrows are native uGUI navigation
    /// across the cursors, Submit picks, focus pins to the commit button while a
    /// die rests, Submit starts the action, Back retracts) — the mod adds NO
    /// navigation here. It adds: announcements clocked on the flow's own FSM
    /// states (signals are clocks; words come from render), a die read for the
    /// cursors the native focus walks, and the Backspace rungs that mirror the
    /// game's own Back polls by firing the exact events those polls fire.
    ///
    /// Corpus-derived, PENDING LIVE VALIDATION (D11 §9 list).</summary>
    internal static class DiceFlow
    {
        private static float _pickerSpokeAt = -10f;

        public static void Init()
        {
            // Picker opened: any of the three systems entering Active. All three
            // activate together (one card slot broadcasts Activate to all), so a
            // short window dedupes the triple signal into one utterance.
            FsmSignals.Subscribe(null, "Active", (fsm, s) =>
            {
                if (!IsDiceSystem(fsm)) return;
                if (Time.unscaledTime - _pickerSpokeAt < 0.5f) return;
                _pickerSpokeAt = Time.unscaledTime;
                SpeechService.Say(Lex.T("dice.picker"), Priority.Queued, "dice");
            });

            // Designed cancel-out: the Reselector is the system's exit state.
            FsmSignals.Subscribe(null, "Reselector", (fsm, s) =>
            {
                if (!IsDiceSystem(fsm)) return;
                if (Time.unscaledTime - _closedSpokeAt < 0.5f) return;
                _closedSpokeAt = Time.unscaledTime;
                SpeechService.Say(Lex.T("dice.picker-closed"), Priority.Queued, "dice");
            });

            // Cursor refusal: Used Animation is the rendered no (spent or broken
            // die — the cursor treats Broken as Used, D11 §2.4).
            FsmSignals.Subscribe(null, "Used Animation", (fsm, s) =>
            {
                if (fsm == null || !fsm.gameObject.name.StartsWith("Dice Cursor")) return;
                SpeechService.Say(Lex.T("dice.refused"), Priority.Queued, "dice");
            });

            // Die resting: the card's controller reaches Slotted — the state ALL
            // controller variants share (sync review MED-5: the cryo and small
            // variants have no Slotted Idle and "Action Cryo Controller" fails a
            // name filter; the class key is the Action Identifier variable,
            // 363/363 controllers, D2). Speak the rendered outlook and the
            // rendered commit-button label.
            FsmSignals.Subscribe(null, "Slotted", (fsm, s) =>
            {
                if (!IsActionController(fsm)) return;
                var card = fsm.transform.parent;
                var sb = new System.Text.StringBuilder(Lex.T("dice.slotted"));
                if (card != null)
                {
                    // The odds render in the band/bucket anatomy the dialogue tier
                    // already transcodes (ride V1 fix: text is never on the
                    // container itself).
                    string odds = SkillChecks.ReadOdds(FindDeep(card, "DICE percentages"));
                    if (odds != null) sb.Append(' ').Append(odds);
                    var button = FindDeep(card, "Dice Slot Button");
                    string label = button != null && button.gameObject.activeInHierarchy
                        ? Describe.FirstText(button.gameObject) : null;
                    if (label != null) sb.Append(' ').Append(label).Append('.');
                }
                SpeechService.Say(sb.ToString(), Priority.Queued, "dice");
            });

            // Retraction / bounce: the controller's Unslot Dice covers the Back
            // retract, the Value Check refusal, and the one-allocation eviction.
            // Its own announcement so it never sounds like a cancel (CS1 ruling).
            FsmSignals.Subscribe(null, "Unslot Dice", (fsm, s) =>
            {
                if (!IsActionController(fsm)) return;
                SpeechService.Say(Lex.T("dice.returned"), Priority.Queued, "dice");
            });
        }

        private static float _closedSpokeAt = -10f;

        /// <summary>Controller class by its Action Identifier variable (D2:
        /// 363/363 across all variants), never by owner name.</summary>
        private static bool IsActionController(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject == null) return false;
            var id = fsm.FsmVariables.GetFsmString("Action Identifier");
            return id != null && !string.IsNullOrEmpty(id.Value);
        }

        private static bool IsDiceSystem(PlayMakerFSM fsm)
            => fsm != null && fsm.gameObject != null
               && fsm.gameObject.name.EndsWith("Dice Gamepad System");

        /// <summary>Die read for a focused dice cursor: "Die 3, 5." / "Crew 1 die
        /// 2, 4." — value from the die's own DiceValue (its rendered face; 9 IS the
        /// glitched render, D11 §6), state flags from the die FSM's literal Used /
        /// Broken states. Null off-family.</summary>
        public static string CursorRead(GameObject go)
        {
            if (go == null || !go.name.StartsWith("Dice Cursor")) return null;
            PlayMakerFSM dieFsm = null;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                var v = fsm.FsmVariables.GetFsmGameObject("Die");
                if (v != null && v.Value != null)
                {
                    dieFsm = v.Value.GetComponent<PlayMakerFSM>();
                    break;
                }
            }
            if (dieFsm == null) return null;

            var sb = new System.Text.StringBuilder();
            string crew = CrewPrefixOf(go.transform);
            float index = Util.TrailingInt(go.name);
            if (crew != null)
                sb.Append(crew).Append(' ').Append(Lex.T("dice.die-lower"));
            else
                sb.Append(Lex.T("dice.die"));
            if (index > 0) sb.Append(' ').Append((int)index);

            string state = dieFsm.ActiveStateName ?? "";
            var value = dieFsm.FsmVariables.GetFsmFloat("DiceValue");
            if (state == "Used") sb.Append(", ").Append(Lex.T("dice.spent"));
            else if (state == "Broken") sb.Append(", ").Append(Lex.T("dice.broken"));
            else if (value != null)
                sb.Append(", ").Append(value.Value == 9f
                    ? Lex.T("dice.glitched") : value.Value.ToString("0"));
            sb.Append('.');
            return sb.ToString();
        }

        private static string CrewPrefixOf(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                string n = cur.name;
                if (n.EndsWith("Dice Gamepad System") && n.StartsWith("Crew "))
                    return n.Substring(0, n.IndexOf(" Dice"));
            }
            return null;
        }

        /// <summary>Backspace during allocation: fire exactly the events the game's
        /// own Back polls fire (D11 §4) — Reset to an engaged card slot (retract /
        /// close picker from the slot side), else Back to every Active system
        /// (Reselector out). Never an invented input: same events, same states.
        /// Returns false when nothing was engaged so the caller falls through to
        /// the next cancel rung — a Backspace must never be swallowed (ride V1:
        /// transient system states ate presses).</summary>
        /// <summary>One physical Back press is seen by EVERY polling rung at once
        /// (D11 §4's ButtonDown/ButtonUp table) — so one Backspace fires every
        /// applicable rung's own event in the same beat: cursors drop their pickup
        /// (DragReset), slots reset, systems Back (the ButtonUp half that re-arms
        /// the picker after a retract — sync review MED-7: sending only the slot
        /// half closed the picker instead of reopening it).</summary>
        private static string _lastRungSignature;
        private static float _lastRungAt = -10f;

        public static bool CancelRung()
        {
            bool sent = false;
            var signature = new System.Text.StringBuilder();
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (fsm == null || fsm.gameObject == null) continue;
                if (!fsm.gameObject.scene.IsValid()) continue;
                if (!fsm.gameObject.activeInHierarchy) continue;
                string name = fsm.gameObject.name;
                string state = fsm.ActiveStateName;
                if (name == "Gamepad Dice Slot")
                {
                    if (state == "Select Dice" || state == "Select Dice 2" || state == "Slot Item")
                    {
                        fsm.SendEvent("Reset");
                        sent = true;
                        signature.Append(fsm.GetInstanceID()).Append(':').Append(state).Append(';');
                    }
                }
                else if (name.StartsWith("Dice Cursor"))
                {
                    if (state == "Select Dice" || state == "Slot Die")
                    {
                        fsm.SendEvent("DragReset");
                        sent = true;
                        signature.Append(fsm.GetInstanceID()).Append(':').Append(state).Append(';');
                    }
                }
            }
            foreach (var system in GameQueries.DiceSystems())
            {
                if (system == null || system.gameObject == null
                    || !system.gameObject.activeInHierarchy) continue;
                string state = system.ActiveStateName;
                if (state == "Active" || state == "Slotted")
                {
                    system.SendEvent("Back");
                    sent = true;
                    signature.Append(system.GetInstanceID()).Append(':').Append(state).Append(';');
                }
            }
            if (!sent)
            {
                Plugin.Log.LogInfo("[Dice] Backspace: no engaged slot/system — falling through");
                return false;
            }
            // Repeat guard (ride finding, cantine item slot): firing the exact
            // same events into the exact same unmoved states twice in a beat
            // means the events aren't landing — fall through to the next rung
            // instead of walling the player (a press is NEVER swallowed twice
            // by the same wall). The miss is a capture.
            string sig = signature.ToString();
            if (sig == _lastRungSignature && Time.unscaledTime - _lastRungAt < 1.5f)
            {
                Plugin.Log.LogWarning("[Dice] cancel rung repeated with no state change ("
                    + sig + ") — falling through, capture needed");
                return false;
            }
            _lastRungSignature = sig;
            _lastRungAt = Time.unscaledTime;
            return true;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
