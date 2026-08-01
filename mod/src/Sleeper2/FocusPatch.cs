using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Announce-on-focus for CS2 — the minimal CS1 policy set: user-move
    /// window, boot-sweep silence, settle deferral for game-driven bursts, and the
    /// re-announce cooldown. CS1's surface-specific suppressions (cycle sweep, cloud
    /// flight, map table, strip steal, dice mute) return one at a time as their
    /// surfaces are ported, each re-verified against CS2 behavior first.</summary>
    [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.SetSelectedGameObject),
        typeof(GameObject), typeof(BaseEventData))]
    internal static class FocusPatch
    {
        /// <summary>The game shuffles selection between the same few objects; don't
        /// re-announce an object focused again this recently (game-driven only).</summary>
        private const float ReannounceCooldown = 2.5f;
        private const float FocusSettle = 0.25f;

        private static readonly Dictionary<int, float> LastAnnounced = new Dictionary<int, float>();
        private static float _userMoveExpires = -1f;

        /// <summary>Called by Navigator (via NavSignals) immediately before it moves or
        /// sets selection — only selection changes the mod causes count as user-initiated.</summary>
        public static void NoteUserNavigation() => _userMoveExpires = Time.unscaledTime + Timing.UserMoveWindow;

        // Boot-sweep silence (CS1 W2 hardening): after a scene load the game walks
        // focus across controls the player can't touch yet. Game-driven focus stays
        // silent until the player's first input; user navigation always speaks.
        private static bool _sceneSettled;

        public static void OnSceneChanged() => _sceneSettled = false;

        public static void MarkSettled()
        {
            if (_sceneSettled) return;
            _sceneSettled = true;
            Plugin.Log.LogInfo("[Focus] scene settled (first user input).");
        }

        // Settle idiom: game-driven focus arrives in bursts — defer briefly and speak
        // only the endpoint: whatever is STILL selected and alive at settle. Doomed
        // objects fail the liveness check and drop silently. User nav never defers.
        private static GameObject _pendingFocus;
        private static float _pendingFocusAt;

        public static void Tick()
        {
            if (_pendingFocus != null && Time.unscaledTime >= _pendingFocusAt)
            {
                var go = _pendingFocus;
                _pendingFocus = null;
                if (go != null && go.activeInHierarchy && EventSystem.current != null
                    && EventSystem.current.currentSelectedGameObject == go)
                {
                    string d = ElementDescriber.Element(go, false);
                    if (!string.IsNullOrEmpty(d))
                        SpeechService.Say(d, Priority.Queued, "focus");
                }
                else if (go != null)
                {
                    Plugin.Log.LogInfo("[Focus] settle-dropped: " + go.name);
                }
            }
        }

        /// <summary>Called by Navigator (via NavSignals) before user-initiated
        /// selection so it always announces.</summary>
        public static void ClearCooldown(GameObject go)
        {
            if (go != null) LastAnnounced.Remove(go.GetInstanceID());
        }

        private static void Postfix(GameObject selected)
        {
            if (!Plugin.AnnounceFocus.Value) return;
            if (selected == null) return;

            // Scrollbars carry no information.
            if (selected.GetComponent<UnityEngine.UI.Scrollbar>() != null) return;

            // CS1: the game re-selects Continue after every dialogue advance — never
            // informative. CS2 dialogue path is the same DS layer; carried, verify live.
            if (selected.name == "Continue Button") return;

            // Tutorial modals: the TutorialReader owns that surface — its buffer
            // enqueues the whole box (including the CONTINUE block); the raw focus
            // announcement would duplicate it.
            if (Util.HasAncestor(selected, "Tutorial System")) return;

            // Zone-table camera drives: the table already spoke the node; the
            // selector's follow-up selection of its Location Button is an echo
            // (the CS1 map-table suppression, ported). Manual WASD walks are
            // outside the window and speak normally.
            if (ZoneTable.SuppressLocationFocus(selected))
            {
                Plugin.Log.LogInfo("[Focus] zone-drive echo suppressed: " + selected.name);
                return;
            }

            bool userInitiated = Time.unscaledTime < _userMoveExpires;

            if (!userInitiated && !_sceneSettled)
            {
                Plugin.Log.LogInfo("[Focus] suppressed (boot sweep): " + selected.name);
                return;
            }

            // Cycle transition: the pipeline's Check Variables broadcast and clock
            // wake pulses drive a scene-wide focus flurry (D4 — the CS1 flurry-gate
            // target). Game-driven selections stay silent until the controller is
            // back in Idle; the future cycle summary speaks the outcome instead.
            if (!userInitiated && GameQueries.CycleTransitioning())
            {
                Plugin.Log.LogInfo("[Focus] suppressed (cycle transition): " + selected.name);
                return;
            }

            int id = selected.GetInstanceID();
            float now = Time.unscaledTime;
            if (!userInitiated &&
                LastAnnounced.TryGetValue(id, out float last) && now - last < ReannounceCooldown)
                return;
            LastAnnounced[id] = now;
            if (LastAnnounced.Count > 200) LastAnnounced.Clear();

            if (!userInitiated)
            {
                _pendingFocus = selected;
                _pendingFocusAt = Time.unscaledTime + FocusSettle;
                return;
            }

            string description = ElementDescriber.Element(selected, false);
            if (string.IsNullOrEmpty(description)) return;

            SpeechService.Say(description, Priority.Immediate, "focus");
        }
    }
}
