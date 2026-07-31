using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The location (action view) table — the CS1 D4 stacked grid ported
    /// to CS2 (CS1 owner rulings 2026-07-20 carry; CS2 parameters from the §7b
    /// action-view capture and decode D2): ONE grid, action cards on top, clock
    /// cards below — in CS2 they are literally siblings in the location's group
    /// container (owner catch, Session 3). Crossing the boundary announces the
    /// section and resets the column. Arrows walk, full read on row switch,
    /// Space = detail (adds the rendered OUTCOMES block), Enter = row commit —
    /// one native click on the card's own button: die actions enter the game's
    /// dice-first flow, cryo/item actions run their designed flow. Clock rows are
    /// display-only. Skill-locked cards refuse with the card's own rendered lock
    /// text instead of clicking into a doomed picker (CS1 ruling).
    ///
    /// Group discovery is mechanism-keyed (D2): the open location's marker FSM
    /// sits in "Selected" and carries its own Location Actions pointer — never a
    /// name match. Cards are children owning an Action Identifier FSM (363/363);
    /// clock cards render a Clock Name; everything else in the group (connectors,
    /// notifications, stress meters, tutorial triggers) is not a row.
    ///
    /// Corpus-derived, PENDING LIVE VALIDATION. Loud seams: unresolved group,
    /// clock without readable progress, card without a button.</summary>
    internal static class LocationTable
    {
        private const float CacheWindow = 0.4f;

        private static readonly string[] ActionHeaderKeys =
            { "loc.col.name", "loc.col.requires", "loc.col.risk",
              "loc.col.cost", "loc.col.predicted", "loc.col.narrative" };
        private static readonly string[] ClockHeaderKeys =
            { "loc.col.name", "loc.col.progress", "loc.col.narrative" };

        private static readonly List<Transform> _actions = new List<Transform>();
        private static readonly List<Transform> _clocks = new List<Transform>();
        private static float _builtAt = -1f;
        private static Transform _group;
        private static bool _entered;

        private static bool ClockRowAt(int row) => row >= Actions().Count;

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Actions().Count + Clocks().Count,
            Cols = row => ClockRowAt(row) ? ClockHeaderKeys.Length : ActionHeaderKeys.Length,
            SectionOf = row => ClockRowAt(row) ? 1 : 0,
            SectionPrefix = s => Lex.T(s == 1 ? "loc.section.clocks" : "loc.section.actions"),
            RowSpeech = (row, col) => RowRead(row, col),
            CellSpeech = (row, col) => CellRead(row, col),
            Detail = (row, _) => Table.Say(DetailRead(row)),
            Commit = (row, _) => Commit(row),
            EmptyRow = () => Lex.T("loc.empty"),
            EmptyCol = () => Lex.T("loc.empty"),
            EmptyDetail = () => Lex.T("loc.empty"),
            EmptyCommit = () => Lex.T("loc.empty"),
            Source = "location",
        };

        /// <summary>Action view is up (the game's own global dial), no conversation,
        /// no tutorial. The zone table's Active() is false under the same dial, so
        /// exactly one of the two owns the keys.</summary>
        public static bool Active()
        {
            if (ConversationEvents.ConversationActive) return false;
            if (TutorialReader.Active()) return false;
            var actionView = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmBool("Action View?");
            return actionView != null && actionView.Value;
        }

        public static bool HandleKeys()
        {
            if (!_entered)
            {
                _entered = true;
                Table.Reset();
                // Landing read: the section prefix + top row, same shape a row
                // arrival speaks (the native focus is already on a die slot —
                // the table is the browse layer above it).
                if (Actions().Count + Clocks().Count > 0)
                    Table.Say(Lex.T("loc.section.actions") + " " + RowRead(0, 0));
                else
                    Table.Say(Lex.T("loc.empty"));
                return true;
            }
            return Table.HandleKeys();
        }

        public static void Tick()
        {
            if (!Active() && _entered)
            {
                _entered = false;
                _group = null;
                _builtAt = -1f;
                Table.Reset();
            }
        }

        // ---------- Rows (fetched fresh per keypress; group cached briefly) ----------

        private static List<Transform> Actions() { Build(); return _actions; }
        private static List<Transform> Clocks() { Build(); return _clocks; }

        private static void Build()
        {
            if (Time.unscaledTime - _builtAt <= CacheWindow) return;
            _builtAt = Time.unscaledTime;
            _actions.Clear();
            _clocks.Clear();
            var group = OpenGroup();
            if (group == null) return;
            foreach (Transform child in group)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                if (StationAtlas.ActionIdentifierOf(child) != null) _actions.Add(child);
                else if (Describe.TextUnder(child, "Clock Name") != null) _clocks.Add(child);
                // else: connectors, notifications, stress meters, triggers — not rows.
            }
        }

        /// <summary>The open location's card group. Preferred: the marker FSM in
        /// "Selected" carries its own Location Actions pointer (D2 — never a name
        /// match). Fallback: the single active group under an action-groups
        /// container; multiple/none logs loudly.</summary>
        private static Transform OpenGroup()
        {
            if (_group != null && _group.gameObject.activeInHierarchy) return _group;
            _group = null;

            foreach (var node in StationAtlas.Build())
            {
                if (node.State != "Selected" || node.Root == null) continue;
                foreach (var fsm in node.Root.GetComponents<PlayMakerFSM>())
                {
                    var v = fsm.FsmVariables.GetFsmGameObject("Location Actions");
                    if (v != null && v.Value != null)
                    {
                        _group = v.Value.transform;
                        return _group;
                    }
                }
            }

            // Fallback: active group children of the action-group containers.
            var candidates = new List<Transform>();
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (t.name != "1_Action Groups" && t.name != "Rig Action Groups") continue;
                foreach (Transform group in t)
                    if (group.gameObject.activeInHierarchy)
                        candidates.Add(group);
            }
            if (candidates.Count == 1) { _group = candidates[0]; return _group; }
            LogOnce("[Location] group unresolved: no Selected marker pointer, "
                + candidates.Count + " active group(s) — capture needed");
            return null;
        }

        // ---------- Speech ----------

        private static string RowRead(int row, int col)
        {
            var actions = Actions();
            if (row < actions.Count)
            {
                var card = actions[row];
                return col <= 0 ? ActionRow(card)
                    : ActionName(card) + ". " + CellRead(row, col);
            }
            int ci = row - actions.Count;
            if (ci >= Clocks().Count) return Lex.T("loc.empty");
            var clock = Clocks()[ci];
            return col <= 0 ? ClockRow(clock)
                : ClockName(clock) + ". " + CellRead(row, col);
        }

        private static string CellRead(int row, int col)
        {
            var actions = Actions();
            bool isClock = row >= actions.Count;
            var keys = isClock ? ClockHeaderKeys : ActionHeaderKeys;
            if (col < 0 || col >= keys.Length) col = 0;
            string content = isClock
                ? ClockCell(Clocks()[row - actions.Count], col)
                : ActionCell(actions[row], col);
            return Lex.T(keys[col]) + ": " + (content ?? Lex.T("loc.none"));
        }

        /// <summary>Space: the row plus the rendered OUTCOMES block and odds, when
        /// the card shows them (render-gated — CS1 detail idiom).</summary>
        private static string DetailRead(int row)
        {
            var actions = Actions();
            if (row >= actions.Count)
            {
                int ci = row - actions.Count;
                return ci < Clocks().Count ? ClockRow(Clocks()[ci]) : Lex.T("loc.empty");
            }
            var card = actions[row];
            var sb = new System.Text.StringBuilder(ActionRow(card));
            var outcomes = card.Find("OUTCOMES") ?? FindDeep(card, "OUTCOMES");
            if (outcomes != null && outcomes.gameObject.activeInHierarchy)
            {
                foreach (var tmp in outcomes.GetComponentsInChildren<TMP_Text>(false))
                {
                    string t = SpeechService.Clean(tmp.text);
                    if (!string.IsNullOrEmpty(t)) sb.Append(' ').Append(t).Append('.');
                }
            }
            string odds = Describe.TextUnder(card, "DICE percentages");
            if (odds != null) sb.Append(' ').Append(odds).Append('.');
            return sb.ToString();
        }

        private static void Commit(int row)
        {
            var actions = Actions();
            if (actions.Count == 0 || row >= actions.Count)
                return; // clock rows are display-only (CS1 ruling)
            var card = actions[row];
            // Skill-locked cards refuse with the card's own rendered lock text
            // (CS1 doomed-picker ruling; CS2 renders it in the Skill Lock element).
            string lockText = SkillLockText(card);
            if (lockText != null)
            {
                SpeechService.Say(lockText + ".", Priority.Immediate, "location");
                return;
            }
            var button = CardButton(card);
            if (button != null)
            {
                Navigator.Click(button);
                return;
            }
            SpeechService.Say(Lex.T("loc.card-disabled"), Priority.Immediate, "location");
        }

        // ---------- Action cells (rendered card anatomy, §7b) ----------

        private static string ActionName(Transform card)
            => Describe.TextUnder(card, "Action Name") ?? card.name.TrimEnd();

        /// <summary>Full read on row switch (CS1 ruling): every populated facet in
        /// column order, narrative last; disabled rows carry their reason.</summary>
        private static string ActionRow(Transform card)
        {
            var sb = new System.Text.StringBuilder(ActionName(card)).Append('.');
            string requires = RequiresCell(card);
            if (requires != null)
                sb.Append(' ').Append(Lex.T("loc.col.requires")).Append(' ')
                  .Append(requires).Append('.');
            string risk = RiskCell(card);
            if (risk != null) sb.Append(' ').Append(risk).Append('.');
            string lockText = SkillLockText(card);
            if (lockText != null) sb.Append(' ').Append(lockText).Append('.');
            string cost = CostCell(card);
            if (cost != null) sb.Append(' ').Append(cost).Append('.');
            string predicted = PredictedCell(card);
            if (predicted != null)
                sb.Append(' ').Append(Lex.T("loc.col.predicted")).Append(": ")
                  .Append(predicted).Append('.');
            string narrative = Describe.TextUnder(card, "Description");
            if (narrative != null) sb.Append(' ').Append(narrative);
            return sb.ToString();
        }

        private static string ActionCell(Transform card, int col)
        {
            switch (col)
            {
                case 0: return ActionRow(card);
                case 1: return RequiresCell(card);
                case 2: return RiskCell(card);
                case 3: return CostCell(card);
                case 4: return PredictedCell(card);
                default: return Describe.TextUnder(card, "Description");
            }
        }

        /// <summary>The merged requirements cell (CS1 ruling): the take-kind first —
        /// die slots are pure structure (a Gamepad Dice Slot child), never a blanket
        /// phrase for non-die cards — then the rendered skill + modifier tier.</summary>
        private static string RequiresCell(Transform card)
        {
            var parts = new List<string>();
            if (FindDeep(card, "Gamepad Dice Slot") != null)
                parts.Add(Lex.T("loc.takes-die"));
            string skill = Describe.TextUnder(card, "Skill");
            if (skill != null)
            {
                // Modifier tier: the Final display renders the live value; Preview
                // is the mid-hover variant (§7b) — read whichever is active.
                string tier = Describe.TextUnder(card, "Final")
                           ?? Describe.TextUnder(card, "Preview");
                parts.Add(tier != null ? skill + " " + tier : skill);
            }
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : null;
        }

        private static string RiskCell(Transform card)
        {
            var rating = Describe.TextUnder(card, "Rating Name");
            string risk = rating != null ? rating.ToLowerInvariant() : null;
            string badge = Describe.TextContaining(card, "CRITICAL");
            if (badge != null)
                risk = risk != null ? risk + ", " + badge.ToLowerInvariant()
                                    : badge.ToLowerInvariant();
            return risk;
        }

        /// <summary>Cryo/item cost — rendered Cost Label (CS1 idiom, D2 small/cryo
        /// controller variants); the PER CYCLE strip if that's what renders.</summary>
        private static string CostCell(Transform card)
            => Describe.TextUnder(card, "Cost Label")
               ?? Describe.TextContaining(card, "PER CYCLE");

        /// <summary>The PREDICTIVE perk block — render-gated by the game (perk
        /// bought = it renders; otherwise silent, CS1 Intuit-cell ruling).</summary>
        private static string PredictedCell(Transform card)
        {
            var block = FindDeep(card, "PREDICTIVE");
            if (block == null || !block.gameObject.activeInHierarchy) return null;
            var parts = new List<string>();
            foreach (var tmp in block.GetComponentsInChildren<TMP_Text>(false))
            {
                string t = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(t)) parts.Add(t);
            }
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : null;
        }

        /// <summary>Rendered lock text when the Skill Lock element is up and carries
        /// lock words (it also renders bare "+1" upgrade hints — those are not a
        /// refusal; verify-live).</summary>
        private static string SkillLockText(Transform card)
        {
            string text = Describe.TextUnder(card, "Skill Lock");
            if (text == null) return null;
            string upper = text.ToUpperInvariant();
            return upper.Contains("LOCK") || upper.Contains("REQUIRED") ? text : null;
        }

        /// <summary>The card's own commit target: the dice-slot button for die
        /// actions (the game's dice-first entry), else the first interactable
        /// Button under the card (cryo/item variants).</summary>
        private static GameObject CardButton(Transform card)
        {
            var slotButton = FindDeep(card, "Dice Slot Button");
            if (slotButton != null && slotButton.gameObject.activeInHierarchy)
                return slotButton.gameObject;
            foreach (var b in card.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                if (b.IsInteractable()) return b.gameObject;
            return null;
        }

        // ---------- Clock cells ----------

        private static string ClockName(Transform clock)
            => Describe.TrimQuotes(Describe.TextUnder(clock, "Clock Name")) ?? clock.name;

        private static string ClockNarrative(Transform clock)
            => Describe.TextUnder(clock, "Clock Description")
               ?? Describe.TextUnder(clock, "Description");

        private static string ClockRow(Transform clock)
        {
            string progress = ClockProgress(clock);
            string narrative = ClockNarrative(clock);
            return ClockName(clock) + (progress != null ? ", " + progress : "") + "."
                 + (narrative != null ? " " + narrative : "");
        }

        private static string ClockCell(Transform clock, int col)
        {
            switch (col)
            {
                case 0: return ClockRow(clock);
                case 1: return ClockProgress(clock);
                default: return ClockNarrative(clock);
            }
        }

        /// <summary>Progress from the card's own N Step Clock: steps from the
        /// element's name (the game's own naming — "6 Step Clock"), value from its
        /// FSM's clock-named numeric variable. No readable value = name-only row
        /// plus a capture log (transcode seam, never a guess).</summary>
        private static string ClockProgress(Transform clock)
        {
            Transform stepClock = null;
            foreach (var t in clock.GetComponentsInChildren<Transform>(false))
                if (t.name.EndsWith("Step Clock")) { stepClock = t; break; }
            if (stepClock == null) return null;
            float steps = Util.LeadingInt(stepClock.name);
            foreach (var fsm in stepClock.GetComponents<PlayMakerFSM>())
            {
                foreach (var f in fsm.FsmVariables.FloatVariables)
                    if (f.Name.IndexOf("Clock", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return Progress(f.Value, steps);
                foreach (var i in fsm.FsmVariables.IntVariables)
                    if (i.Name.IndexOf("Clock", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return Progress(i.Value, steps);
            }
            LogOnce("[Location] step clock \"" + stepClock.name
                + "\" has no clock-named numeric var — capture needed");
            return null;
        }

        private static string Progress(float value, float steps)
            => steps > 0f
                ? value.ToString("0.#") + " " + Lex.T("vitals.of") + " " + steps.ToString("0.#")
                : value.ToString("0.#");

        // ---------- Plumbing ----------

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void LogOnce(string line)
        {
            if (Logged.Add(line)) Plugin.Log.LogWarning(line);
        }
    }
}
