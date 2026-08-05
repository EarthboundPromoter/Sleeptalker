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

        // Identical-announce window (log finding 2026-08-02: "BACK button"
        // spoke 9× in 3.7s after the CONTRACT INCOMPLETE popup — selection
        // re-assertion jitter and key-repeat against a one-item menu both
        // produce the same object over and over). The SAME object with the
        // SAME description within the window stays silent; any different
        // object, or changed text on the same object, always speaks.
        private const float IdenticalWindow = 1.0f;
        private static int _lastFocusId;
        private static string _lastFocusText;
        private static float _lastFocusAt = -10f;

        private static bool IdenticalRepeat(GameObject go, string description)
        {
            float now = Time.unscaledTime;
            if (go.GetInstanceID() == _lastFocusId && description == _lastFocusText
                && now - _lastFocusAt < IdenticalWindow)
            {
                _lastFocusAt = now; // a sustained storm stays suppressed
                return true;
            }
            _lastFocusId = go.GetInstanceID();
            _lastFocusText = description;
            _lastFocusAt = now;
            return false;
        }

        /// <summary>Called by Navigator (via NavSignals) immediately before it moves or
        /// sets selection — only selection changes the mod causes count as user-initiated.</summary>
        public static void NoteUserNavigation() => _userMoveExpires = Time.unscaledTime + Timing.UserMoveWindow;

        // Boot-sweep silence (CS1 W2 hardening): after a scene load the game walks
        // focus across controls the player can't touch yet. Game-driven focus stays
        // silent until the player's first input; user navigation always speaks.
        private static bool _sceneSettled;

        /// <summary>Has the player taken control of this scene yet? Anything the
        /// game does before that is load-time construction, not an event.</summary>
        public static bool SceneSettled => _sceneSettled;

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
        private static int _pendingRetries;

        public static void Tick()
        {
            if (_pendingFocus != null && Time.unscaledTime >= _pendingFocusAt)
            {
                var go = _pendingFocus;
                _pendingFocus = null;
                if (go != null && go.activeInHierarchy && EventSystem.current != null
                    && EventSystem.current.currentSelectedGameObject == go)
                {
                    // Render gate on the game-driven settle path (the Solheim
                    // class generally: an element the screen isn't drawing is
                    // never news — active ≠ rendered, the founding trap).
                    // User-initiated reads stay ungated: the mod only ever
                    // navigates rendered rows. A still-fading element re-arms
                    // rather than drops (sync pass MED-6: fades longer than
                    // one settle window exist) — bounded retries.
                    if (!Util.RenderedUp(go.transform))
                    {
                        if (_pendingRetries < 4)
                        {
                            _pendingRetries++;
                            _pendingFocus = go;
                            _pendingFocusAt = Time.unscaledTime + FocusSettle;
                        }
                        else
                        {
                            Plugin.Log.LogInfo("[Focus] settle-dropped (never rendered): " + go.name);
                        }
                        return;
                    }
                    _pendingRetries = 0;
                    string d = DescribeFocused(go);
                    if (!string.IsNullOrEmpty(d) && !IdenticalRepeat(go, d))
                    {
                        // A census change held while the player was away
                        // composes AHEAD of the returning node read (owner
                        // ruling 2026-08-04) — one utterance, callout first.
                        // The composed line rides the DURABLE census source
                        // (delta-pass HIGH: on the volatile focus source a
                        // modal claim or a focus flush could destroy the
                        // callout after the census forgot it).
                        string census = StationCensus.ComposePrefix();
                        if (census != null)
                            SpeechService.Say(census + " " + d, Priority.Queued, "census");
                        else
                            SpeechService.Say(d, Priority.Queued, "focus");
                    }
                }
                else if (go != null)
                {
                    _pendingRetries = 0;
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

            // Invisible gamepad focus anchors (map + character window, D8/D18):
            // pure selection parking, nothing rendered to speak.
            if (selected.name == "Gamepad Selection Button") return;

            // Post-resolution slot landing (D14: the slot FSM re-selects its own
            // Gamepad Dice Slot a few frames after the outcome — a designed
            // parking, not news; it must not interject in the outcome utterance).
            if (selected.name == "Gamepad Dice Slot" && ActionOutcomes.ResolutionInFlight)
                return;

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

            // Map-table select-follow: same suppression, map flavor — the table
            // already spoke the row; the selection it sets is the follow, not news.
            if (MapTable.SuppressMarkerFocus(selected))
            {
                Plugin.Log.LogInfo("[Focus] map-follow echo suppressed: " + selected.name);
                return;
            }

            // Closed-map marker leak (SOLHEIM capture 2026-08-02): the closed
            // Map Screen hides by CanvasGroup alpha ONLY (D8 — alpha is not
            // presence) and its marker buttons stay active and UI Button-
            // tagged, so the station's proximity selector can land on an
            // invisible contract marker mid-pan and the focus read spoke it
            // as if it were a station node. Mechanism-class gate: nothing
            // under a CLOSED Map Screen is news. (StationAtlas.Build excludes
            // the same subtree for the same reason.)
            if (!GameQueries.MapOpen() && Util.HasAncestor(selected, "Map Screen"))
            {
                Plugin.Log.LogInfo("[Focus] closed-map echo suppressed: " + selected.name);
                return;
            }

            // Inventory-table select-follow: same idiom — the Item Cursor
            // selection the table set is the inspection hover, not news.
            if (InventoryTable.SuppressCursorFocus(selected))
            {
                Plugin.Log.LogInfo("[Focus] inventory-follow echo suppressed: " + selected.name);
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
                _pendingRetries = 0;
                return;
            }

            string description = DescribeFocused(selected);
            if (string.IsNullOrEmpty(description)) return;
            if (IdenticalRepeat(selected, description)) return;

            // Same composition seam as the settle path, same durable-source
            // routing for the composed form.
            string censusPrefix = StationCensus.ComposePrefix();
            if (censusPrefix != null)
                SpeechService.Say(censusPrefix + " " + description,
                    Priority.Immediate, "census");
            else
                SpeechService.Say(description, Priority.Immediate, "focus");
        }

        /// <summary>Focus description with the crew-selection enhancement
        /// (owner build 2026-08-02, D10 recipe 6): a map Crew Window button's
        /// label alone is anonymous ("SELECT") — prepend the member's rendered
        /// name from the button's per-crew container. Anatomy is provisional-
        /// offline; any miss falls soft to the default read.</summary>
        private static string DescribeFocused(GameObject go)
        {
            string crew = CrewSelectRead(go);
            return crew ?? ElementDescriber.Element(go, false);
        }

        /// <summary>The crew-selection dossier read (owner ruling 2026-08-02:
        /// enriched focus read, NOT a table — native arrows keep the walking).
        /// Composes everything the button renders: name, status (with the
        /// locked-in translation), stress from the button's own bar variant,
        /// skills from the drawn chip nodes: "NIA. SELECT. Stress 2 of 6.
        /// Intuit plus 1." Falls soft to the default read off-family.</summary>
        private static string CrewSelectRead(GameObject go)
        {
            bool inCrewWindow = false;
            for (var t = go.transform; t != null; t = t.parent)
                if (t.name == "Crew Window" || t.name == "Crew Confrim")
                { inCrewWindow = true; break; }
            if (!inCrewWindow) return null;

            Transform button = null;
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (t.name.StartsWith("Crew Button")) { button = t; break; }
                if (t.name == "Crew Window" || t.name == "Crew Confrim") break;
            }
            if (button == null) return null; // window chrome: default read

            string name = RenderedChildText(button, "Name");
            if (name == null) return null; // fall soft to the default read

            var sb = new System.Text.StringBuilder(name).Append('.');

            // Status. Owner ruling 2026-08-02 (live capture): the game's bare
            // "LOCKED" means locked IN — the button FSM rests in 'Slot N
            // LOCKED' (mandatory crew) — ambiguous beside NOT AVAILABLE, so
            // that one case speaks the state-name truth; all others render.
            var fsm = button.GetComponent<PlayMakerFSM>();
            string state = fsm != null ? fsm.ActiveStateName ?? "" : "";
            if (state.StartsWith("Slot ") && state.EndsWith("LOCKED"))
                sb.Append(' ').Append(Lex.T("crew.locked-in")).Append(' ')
                  .Append(Util.LeadingInt(state.Substring(5)).ToString("0")).Append('.');
            else
            {
                string label = ElementDescriber.Element(go, false);
                if (!string.IsNullOrEmpty(label)) sb.Append(' ').Append(label).Append('.');
            }

            // Stress from the button's OWN bar variant (same family as the
            // contract panels; caps byte-true per variant name).
            foreach (var t in button.GetComponentsInChildren<Transform>(false))
            {
                if (!t.name.Contains("Stress") || t.name.Contains("Label")) continue;
                var barFsm = t.GetComponent<PlayMakerFSM>();
                if (barFsm == null) continue;
                var stress = barFsm.FsmVariables.GetFsmFloat("Stress");
                if (stress == null) continue;
                float max = Vitals.CrewMax(barFsm);
                // Bounds ruling 2026-08-02: never speak past the bar.
                float value = stress.Value < 0f ? 0f
                    : (max > 0f && stress.Value > max ? max : stress.Value);
                sb.Append(' ').Append(Lex.T("vitals.stress")).Append(' ')
                  .Append(value.ToString("0.#"));
                if (max > 0f) sb.Append(' ').Append(Lex.T("vitals.of")).Append(' ')
                              .Append(max.ToString("0"));
                sb.Append('.');
                break;
            }

            // Skills: the drawn chip nodes under Crew Skills — the skill node's
            // name is the identity, the active "<+N> Modifier" child the chip.
            var skills = FindChild(button, "Crew Skills");
            if (skills != null)
            {
                foreach (Transform skill in skills)
                {
                    if (!skill.gameObject.activeInHierarchy || !Util.RenderedUp(skill)) continue;
                    string chip = null;
                    foreach (Transform mod in skill)
                    {
                        if (!mod.name.EndsWith("Modifier")) continue;
                        if (!mod.gameObject.activeInHierarchy || !Util.RenderedUp(mod)) continue;
                        string raw = mod.name.Substring(0, mod.name.Length - 8).Trim();
                        chip = raw.StartsWith("+")
                            ? Lex.T("glyph.plus") + " " + raw.Substring(1)
                            : raw;
                        break;
                    }
                    sb.Append(' ').Append(skill.name);
                    if (chip != null) sb.Append(' ').Append(chip);
                    sb.Append('.');
                }
            }
            return sb.ToString();
        }

        private static string RenderedChildText(Transform root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(false))
            {
                if (t.name != childName) continue;
                var tmp = t.GetComponent<TMPro.TMP_Text>();
                if (tmp == null || !Util.RenderedUp(tmp.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(false))
                if (t.name == name) return t;
            return null;
        }
    }
}
