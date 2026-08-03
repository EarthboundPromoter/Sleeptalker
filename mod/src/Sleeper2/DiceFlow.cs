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
                // What got slotted (ride finding: cryo payments spoke "Die
                // slotted"): a die engages the dice systems; an item/cryo payment
                // does not — the engaged-systems dial is the discriminator.
                var sb = new System.Text.StringBuilder(Lex.T(
                    GameQueries.DiceAllocationLive() ? "dice.slotted" : "dice.slotted-item"));
                if (card != null)
                {
                    // The odds render in the band/bucket anatomy the dialogue tier
                    // already transcodes (ride V1 fix: text is never on the
                    // container itself).
                    string odds = SkillChecks.ReadOdds(FindDeep(card, "DICE percentages"));
                    if (odds != null) sb.Append(' ').Append(odds);
                    // Crew skill verdict (owner ruling 2026-08-02, D10 (d)): a
                    // crew die makes the card render a text verdict (skill +
                    // found/proficient/expert/missing) — spoken verbatim when
                    // drawn; player dice draw none, so this stays silent.
                    string verdict = CrewVerdict(card);
                    if (verdict != null) sb.Append(' ').Append(verdict).Append('.');
                    var button = FindDeep(card, "Dice Slot Button");
                    string label = button != null && button.gameObject.activeInHierarchy
                        ? Describe.FirstText(button.gameObject) : null;
                    if (label != null) sb.Append(' ').Append(label).Append('.');
                }
                // Ride V5 bug: the allocation pipeline PASSES THROUGH Unslot
                // Dice on every normal slot — flush the not-yet-spoken "Die
                // returned." and drop any pending-verify returns so neither
                // ever precedes the slot announce.
                SpeechService.FlushSource("dice");
                _returns.Clear();
                SpeechService.Say(sb.ToString(), Priority.Queued, "dice");
            });

            // Retraction / bounce: the controller's Unslot Dice covers the Back
            // retract, the Value Check refusal, the one-allocation eviction —
            // AND the commit pipeline passes through it too (owner report
            // 2026-08-02: "Die returned." spoke on performing an action). So
            // the line VERIFIES before speaking: a few frames later the
            // controller is back at rest after a genuine retraction, but off
            // in the resolution pipeline after a commit — only the rest case
            // speaks. Frame-counted, not wall-clock (the settle-watch idiom).
            FsmSignals.Subscribe(null, "Unslot Dice", (fsm, s) =>
            {
                if (!IsActionController(fsm)) return;
                _returns.Add(new PendingReturn { Controller = fsm });
            });
        }

        /// <summary>Cross-row dice navigation (owner ruling 2026-08-02): native
        /// uGUI nav links only column-aligned dice, so Down from player dice
        /// 3–5 went nowhere. Mapping: Down from player die 1 → crew die 1,
        /// from dice 2–5 → crew die 2; crew row ⇄ crew row aligned; Up from
        /// the top crew row → the aligned player die. An inactive crew row is
        /// skipped; no target = native nav keeps the axis. Selection is
        /// user-initiated (announces immediately).</summary>
        public static bool CrossRowNav(int direction)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected == null || !selected.name.StartsWith("Dice Cursor")) return false;
            int die = (int)Util.TrailingInt(selected.name);
            if (die <= 0) return false;
            int row = -1;
            for (var t = selected.transform; t != null; t = t.parent)
            {
                string n = t.name;
                if (!n.EndsWith("Dice Gamepad System")) continue;
                row = n.StartsWith("Crew 1") ? 1 : n.StartsWith("Crew 2") ? 2 : 0;
                break;
            }
            if (row < 0) return false;

            int fromRow = row;
            int carry = die;
            for (int targetRow = row + direction; targetRow >= 0 && targetRow <= 2;
                 targetRow += direction)
            {
                int targetDie = fromRow == 0 ? (carry == 1 ? 1 : 2) : carry;
                var cursor = FindCursor(targetRow, targetDie);
                if (cursor != null)
                {
                    FocusPatch.NoteUserNavigation();
                    FocusPatch.ClearCooldown(cursor);
                    es.SetSelectedGameObject(cursor);
                    return true;
                }
                fromRow = targetRow;
                carry = targetDie;
            }
            return false;
        }

        private static GameObject FindCursor(int row, int die)
        {
            string system = row == 0 ? "Dice Gamepad System"
                : "Crew " + row + " Dice Gamepad System";
            var fsm = GameQueries.FindFsm(system);
            if (fsm == null || fsm.gameObject == null
                || !fsm.gameObject.activeInHierarchy) return null;
            foreach (var t in fsm.GetComponentsInChildren<Transform>(false))
            {
                if (t.name != "Dice Cursor " + die) continue;
                var button = t.GetComponent<UnityEngine.UI.Button>();
                if (button != null && !button.IsInteractable()) return null;
                return t.gameObject;
            }
            return null;
        }

        private sealed class PendingReturn
        {
            public PlayMakerFSM Controller;
            public int Ticks;
        }

        private static readonly System.Collections.Generic.List<PendingReturn> _returns
            = new System.Collections.Generic.List<PendingReturn>();

        /// <summary>A push fire ForceUnslots every slotted die — those returns
        /// are the push's own mechanics, not retractions (sync pass MED-6).</summary>
        public static void DropPendingReturns(string why)
        {
            if (_returns.Count == 0) return;
            Plugin.Log.LogInfo("[Dice] " + _returns.Count
                + " pending return(s) dropped (" + why + ")");
            _returns.Clear();
        }

        public static void Tick()
        {
            for (int i = _returns.Count - 1; i >= 0; i--)
            {
                var p = _returns[i];
                p.Ticks++;
                if (p.Ticks < 5) continue;
                _returns.RemoveAt(i);
                // Push window (sync pass MED-6): an Unslot observed while the
                // fire composes is the ForceUnslot broadcast — never a return.
                if (PushFlow.FireInFlight)
                {
                    Plugin.Log.LogInfo("[Dice] Unslot during push fire — return line dropped");
                    continue;
                }
                var fsm = p.Controller;
                if (fsm == null || fsm.gameObject == null || !fsm.gameObject.activeInHierarchy)
                    continue; // controller gone — no honest return to report
                string state = fsm.ActiveStateName ?? "";
                if (state == "Idle" || state == "Temp Complete"
                    || state == "Action Completed" || state == "Unslot Dice")
                {
                    SpeechService.Say(Lex.T("dice.returned"), Priority.Queued, "dice");
                }
                else
                {
                    Plugin.Log.LogInfo("[Dice] Unslot pass-through (controller in '"
                        + state + "') — return line dropped");
                }
            }
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
            string cursorFocus = state == "Broken" ? null : FocusedTag(dieFsm);
            if (cursorFocus != null) sb.Append(", ").Append(cursorFocus);
            sb.Append('.');
            if (state != "Broken")
            {
                string health = HealthSuffix(dieFsm.transform);
                if (health != null) sb.Append(' ').Append(health);
            }
            return sb.ToString();
        }

        /// <summary>Tray die read by slot number — shared by the top-bar table and
        /// the cycle summary (render lane: DiceValue face, 9 = glitched, literal
        /// Used/Broken states).</summary>
        public static string DieLine(int n)
        {
            return ReadDieAt(
                "Letterbox Canvas/Top UI/Dice UI/Dice Slot " + n + "/Die",
                Lex.T("dice.die") + " " + n);
        }

        /// <summary>Crew die read (D10 paths, full rooting). Attribution by the
        /// member's RENDERED panel name (owner ruling 2026-08-02); "Crew N" is
        /// the fallback. A resting die REPARENTS into the card's slot (D10 (b))
        /// — never reported absent: the slotted-die global is the same render
        /// object and covers the miss.</summary>
        public static string CrewDieLine(int member, int slot, bool withName = true)
        {
            // Owner ruling 2026-08-02 (the cycle-summary "Crew 1 die" leak):
            // crew data speaks ONLY while the member's panel renders — the
            // persistent die objects outside a contract are garbage at best
            // and pre-rolled values at worst. Gates every consumer (wake
            // summary, V-table rows, push composition).
            if (CrewPanel.PanelOf(member) == null) return null;
            string label = withName
                ? (CrewPanel.NameOf(member) ?? Lex.T("dice.crew") + " " + member)
                    + " " + Lex.T("dice.die-lower") + " " + slot
                : Lex.T("dice.die") + " " + slot;
            string line = ReadDieAt(
                "Letterbox Canvas/Crew UI/Crew Member " + member
                    + "/Display/Crew Dice/Crew - Dice Slot " + slot + "/Die", label);
            if (line != null) return line;
            return ReadSlottedCrewDie(member, slot, label);
        }

        private static string ReadSlottedCrewDie(int member, int slot, string label)
        {
            var g = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmGameObject("SlottedDiceGlobal");
            if (g == null || g.Value == null || !g.Value.activeInHierarchy) return null;
            var fsm = g.Value.GetComponent<PlayMakerFSM>();
            if (fsm == null) return null;
            var dieName = fsm.FsmVariables.GetFsmString("Die Name");
            if (dieName == null || dieName.Value != "Crew" + member + "Die" + slot)
                return null;
            return DieText(fsm, label);
        }

        private static string ReadDieAt(string path, string label)
        {
            var go = GameObject.Find(path);
            if (go == null || !go.activeInHierarchy) return null;
            var fsm = go.GetComponent<PlayMakerFSM>();
            if (fsm == null) return null;
            return DieText(fsm, label);
        }

        private static string DieText(PlayMakerFSM fsm, string label)
        {
            var sb = new System.Text.StringBuilder(label);
            string state = fsm.ActiveStateName ?? "";
            var value = fsm.FsmVariables.GetFsmFloat("DiceValue");
            // RENDER FIRST (ride bug 2026-08-02: crew dice read "0" at wake —
            // spent dice mid-reroll sat in a transitional state with DiceValue
            // still 0, and the variable lane spoke it). The die's own TMP
            // draws the truth: Roman numerals for faces, "-" when spent.
            // Glitch keeps the value lane (a GLITCHED die DRAWS "I" — the
            // value 9 is the only discriminator, D11/decode of record).
            string face = RenderedFace(fsm.gameObject);
            if (state == "Broken") sb.Append(", ").Append(Lex.T("dice.broken"));
            else if (value != null && value.Value == 9f)
                sb.Append(", ").Append(Lex.T("dice.glitched"));
            else if (state == "Used" || face == "-")
                sb.Append(", ").Append(Lex.T("dice.spent"));
            else if (face != null) sb.Append(", ").Append(face);
            else if (value != null && value.Value > 0f)
                sb.Append(", ").Append(value.Value.ToString("0"));
            else sb.Append(", ").Append(Lex.T("dice.spent")); // no face drawn, no value: not usable now
            string focused = state == "Broken" ? null : FocusedTag(fsm);
            if (focused != null) sb.Append(", ").Append(focused);
            sb.Append('.');
            // Die health below full (owner build 2026-08-02; guide: three
            // health, break at zero). Deviation-spoken only; a Broken die
            // already says so. Crew slots have no health family — silent.
            if (state != "Broken")
            {
                string health = HealthSuffix(fsm.transform);
                if (health != null) sb.Append(' ').Append(health);
            }
            return sb.ToString();
        }

        /// <summary>The drawn face of player die slot n, for the push fire
        /// clauses ("Die 3 rerolled, now 5.") — render lane first, value
        /// fallback (glitch keeps its word).</summary>
        public static string FaceOf(int n)
        {
            var go = GameObject.Find("Letterbox Canvas/Top UI/Dice UI/Dice Slot " + n + "/Die");
            if (go == null || !go.activeInHierarchy) return null;
            var fsm = go.GetComponent<PlayMakerFSM>();
            var value = fsm != null ? fsm.FsmVariables.GetFsmFloat("DiceValue") : null;
            if (value != null && value.Value == 9f) return Lex.T("dice.glitched");
            string face = RenderedFace(go);
            if (face != null && face != "-") return face;
            return value != null && value.Value > 0f ? value.Value.ToString("0") : null;
        }

        /// <summary>Every live die FSM rests in a settled state — the wake
        /// summary waits on this (a die mid-reroll sits in Roll/Boost/Glitch
        /// transit with a stale DiceValue). A missing die object counts as
        /// settled; the caller carries the loud backstop.</summary>
        public static bool AllDiceSettled()
        {
            for (int n = 1; n <= 5; n++)
                if (!DieSettledAt("Letterbox Canvas/Top UI/Dice UI/Dice Slot " + n + "/Die"))
                    return false;
            for (int m = 1; m <= 2; m++)
                for (int s = 1; s <= 2; s++)
                    if (!DieSettledAt("Letterbox Canvas/Crew UI/Crew Member " + m
                        + "/Display/Crew Dice/Crew - Dice Slot " + s + "/Die"))
                        return false;
            return true;
        }

        private static bool DieSettledAt(string path)
        {
            var go = GameObject.Find(path);
            if (go == null || !go.activeInHierarchy) return true;
            var fsm = go.GetComponent<PlayMakerFSM>();
            if (fsm == null) return true;
            switch (fsm.ActiveStateName)
            {
                case "Idle":
                case "Used":
                case "Broken":
                case "Highlight":
                case "Slotted":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The face the die actually draws: its TMP renders Roman
        /// numerals I..VI, "-" when spent. Null when nothing readable is
        /// drawn (mid-animation) — callers fall to the value lane.</summary>
        private static string RenderedFace(GameObject die)
        {
            var tmp = die.GetComponent<TMPro.TMP_Text>();
            if (tmp == null) tmp = die.GetComponentInChildren<TMPro.TMP_Text>(false);
            if (tmp == null || !Util.RenderedUp(tmp.transform)) return null;
            string text = SpeechService.Clean(tmp.text);
            if (string.IsNullOrEmpty(text)) return null;
            switch (text)
            {
                case "-": return "-";
                case "I": return "1";
                case "II": return "2";
                case "III": return "3";
                case "IV": return "4";
                case "V": return "5";
                case "VI": return "6";
            }
            LogOnceDice("[Dice] unrecognized die face render \"" + text + "\" — value lane spoken");
            return null;
        }

        private static readonly System.Collections.Generic.HashSet<string> DiceLogged
            = new System.Collections.Generic.HashSet<string>();

        private static void LogOnceDice(string line)
        {
            if (DiceLogged.Add(line)) Plugin.Log.LogWarning(line);
        }

        /// <summary>", focused" while the die's focus marker renders (FOCUS
        /// push, D20: Focus Viz activates a marker GO in the slot; owner
        /// ruling 2026-08-02 — the focused state reports on every die read
        /// until consumed). Render lane judges the marker; the die FSM's own
        /// Focused bool is the logged fallback when no marker object exists
        /// to judge. The suffix drops by construction when the game tears
        /// the marker down.</summary>
        private static string FocusedTag(PlayMakerFSM dieFsm)
        {
            if (dieFsm == null) return null;
            var scope = dieFsm.transform.parent != null
                ? dieFsm.transform.parent : dieFsm.transform;
            bool markerFound = false;
            foreach (var t in scope.GetComponentsInChildren<Transform>(true))
            {
                if (t == dieFsm.transform) continue;
                if (t.name.IndexOf("Focus", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                markerFound = true;
                if (t.gameObject.activeInHierarchy && Util.RenderedUp(t))
                    return Lex.T("dice.focused");
            }
            if (!markerFound)
            {
                var b = dieFsm.FsmVariables.GetFsmBool("Focused");
                if (b != null && b.Value)
                {
                    LogOnceDice("[Dice] no focus marker object under the die's slot — Focused bool lane spoken");
                    return Lex.T("dice.focused");
                }
            }
            return null;
        }

        /// <summary>"Health 2 of 3." from the slot's own Dice Health bar (the
        /// render home; Red at low). Null at full health or where no bar
        /// exists (crew dice).</summary>
        private static string HealthSuffix(Transform die)
        {
            var slot = die != null ? die.parent : null;
            var bar = slot != null ? slot.Find("Dice Health") : null;
            var fsm = bar != null ? bar.GetComponent<PlayMakerFSM>() : null;
            if (fsm == null) return null;
            var health = fsm.FsmVariables.GetFsmFloat("Health");
            if (health == null || health.Value >= 3f || health.Value < 0f) return null;
            return Lex.T("dice.health") + " " + health.Value.ToString("0") + " "
                + Lex.T("vitals.of") + " 3.";
        }

        /// <summary>The rendered crew-slot verdict on a card ("Skilled/Unskilled/
        /// Glitched Dice Slotted" children — CSUI skill + rating words, D10 (d)),
        /// TRANSCODED to value wording (owner ruling 2026-08-02: skills always
        /// read as values — "ENGINEER SKILL MISSING" was a bad reading):
        /// missing → "no skill", found → 0, proficient → plus 1, expert →
        /// plus 2 (the exact boost bands those words render). Unmapped wording
        /// speaks raw, logged. Null when none is drawn.</summary>
        private static string CrewVerdict(Transform card)
        {
            if (card == null) return null;
            foreach (var t in card.GetComponentsInChildren<Transform>(false))
            {
                if (!t.name.EndsWith("Dice Slotted")) continue;
                if (!Util.RenderedUp(t)) continue;
                foreach (var tmp in t.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (!Util.RenderedUp(tmp.transform)) continue;
                    string text = SpeechService.Clean(tmp.text);
                    if (!string.IsNullOrEmpty(text)) return TranscodeVerdict(text);
                }
            }
            return null;
        }

        private static string TranscodeVerdict(string text)
        {
            string upper = text.ToUpperInvariant();
            int space = text.IndexOf(' ');
            string skill = space > 0 ? text.Substring(0, space) : text;
            if (upper.Contains("MISSING") || upper.Contains("NOT FOUND"))
                return skill + ", " + Lex.T("skills.none");
            if (upper.Contains("EXPERT"))
                return skill + ", " + Lex.T("glyph.plus") + " 2";
            if (upper.Contains("PROFICIENT"))
                return skill + ", " + Lex.T("glyph.plus") + " 1";
            if (upper.Contains("FOUND"))
                return skill + ", 0";
            LogOnceDice("[Dice] unmapped crew verdict wording \"" + text + "\" — raw spoken");
            return text;
        }

        private static string CrewPrefixOf(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                string n = cur.name;
                if (n.EndsWith("Dice Gamepad System") && n.StartsWith("Crew "))
                {
                    // Owner ruling 2026-08-02: attribute by the rendered panel
                    // name; the system-name prefix is the logged fallback.
                    int member = (int)Util.LeadingInt(n.Substring(5));
                    string name = member > 0 ? CrewPanel.NameOf(member) : null;
                    return name ?? n.Substring(0, n.IndexOf(" Dice"));
                }
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
