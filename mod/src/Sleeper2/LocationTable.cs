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

        // "Costs" not "Requires" (owner ruling): the cell composes the take-kind
        // directly — "a die", "15 CRYO", "X item" — no verb echo under the header.
        private static readonly string[] ActionHeaderKeys =
            { "loc.col.name", "loc.col.costs", "loc.col.risk",
              "loc.col.cost", "loc.col.predicted", "loc.col.narrative" };
        // Clock columns fused by owner ruling (ride V1): name and progress are ONE
        // cell — "Name, x of y." — narrative beside it. No separate Progress column.
        private static readonly string[] ClockHeaderKeys =
            { "loc.col.name", "loc.col.narrative" };

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

        /// <summary>Key ownership via the ModeModel: this table IS the ActionView
        /// floor; everything above it (pause, tutorial, conversation, dice
        /// allocation, map) outranks by precedence, not by scattered checks.
        /// Overlays are excursions, not exits (CS1 D3): suspension keeps the
        /// cursor; only the view itself closing resets it (see Tick).</summary>
        public static bool Active()
        {
            if (ModeModel.Current() != Mode.ActionView) return false;
            if (TopBarTable.Entered) return false; // V-table excursion
            return true;
        }

        public static bool HandleKeys() => Table.HandleKeys();

        private static Transform _landedGroup;
        // >0 = the entry landing awaits the game's card build-out (owner report
        // 2026-08-01: every location entry spoke "Nothing here." then corrected
        // itself — the Action View? dial flips before the cards materialize, D2).
        private static float _landDeadline = -1f;

        public static void Tick()
        {
            // Entry (and its landing read) happens HERE, not in the key handler —
            // sync review HIGH-3: a landing that consumes the triggering key
            // swallowed the first Backspace after view-open.
            if (!_entered && Active())
            {
                _entered = true;
                _landedGroup = OpenGroup();
                Table.Reset();
                // Deferred landing: the row scan's first content IS the settle
                // event (render arrival, checked per frame) — the deadline only
                // exists so a genuinely empty location still speaks its honest
                // "Nothing here.", logged.
                _landDeadline = Time.unscaledTime + 2f;
                return;
            }
            if (!_entered) return;

            if (ModeModel.ActionViewUp())
            {
                // The pending entry landing: fire the moment cards render.
                if (_landDeadline > 0f)
                {
                    if (!Active()) return; // excursion before landing: hold it
                    _builtAt = -1f;        // rescan the build-out every frame
                    if (Actions().Count + Clocks().Count > 0)
                    {
                        _landDeadline = -1f;
                        _landedGroup = OpenGroup();
                        Land();
                    }
                    else if (Time.unscaledTime >= _landDeadline)
                    {
                        _landDeadline = -1f;
                        _landedGroup = OpenGroup();
                        Plugin.Log.LogInfo("[Location] no cards rendered by the "
                            + "settle deadline — honest empty landing");
                        Land();
                    }
                    return;
                }

                // Content swap under a still-true Action View? global (sync review
                // MED-9: story variants swap whole groups): fresh surface, fresh land.
                var group = OpenGroup();
                if (group != _landedGroup)
                {
                    _landedGroup = group;
                    _builtAt = -1f;
                    Table.Reset();
                    if (Active()) Land();
                }
                return;
            }

            // The view closed: real exit (excursions never reach here — Active()
            // false alone does not reset, CS1 D3).
            _entered = false;
            _group = null;
            _landedGroup = null;
            _builtAt = -1f;
            _landDeadline = -1f;
            Table.Reset();
        }

        /// <summary>The landing read: the top row with ITS OWN section prefix —
        /// a clock-only group lands on "Clock cards.", never a hardcoded
        /// "Action cards." (sync review MED-9).</summary>
        private static void Land()
        {
            int total = Actions().Count + Clocks().Count;
            if (total == 0) { Table.Say(Lex.T("loc.empty")); return; }
            string prefix = Lex.T(ClockRowAt(0) ? "loc.section.clocks" : "loc.section.actions");
            Table.Say(prefix + " " + RowRead(0, 0));
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
                // The End Cycle card carries no Action Identifier and is not a
                // clock, so it fell between both row classes and left the rest
                // node with no reachable rows at all (owner report 2026-08-04).
                // It reads and commits through the ordinary action path — its
                // name, description and Dice Slot Button are the same anatomy.
                if (StationAtlas.ActionIdentifierOf(child) != null
                    || StationAtlas.IsEndCycleCard(child)) _actions.Add(child);
                // Clock rows by FSM class (ClockValue carrier), never by rendered
                // or object name — the CS1 invisible-clock lesson (owner, ride V1).
                else if (GameQueries.ClockFsm(child) != null) _clocks.Add(child);
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

            // Fallback: the group the screen is DRAWING — the same test that told
            // the mode model we are in a location at all. These two MUST agree:
            // if the mode says location and this returns null, the table enters
            // with no rows and speaks "Nothing here." at everything, which is the
            // mirror image of the stranding it was meant to fix.
            _group = StationAtlas.DrawnActionGroup();
            if (_group != null) return _group;
            LogOnce("[Location] group unresolved: no Selected marker pointer and "
                + "no drawn action group — capture needed");
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
            // Status badge in the Costs facet speaks bare (sync pass LOW-8:
            // the row read lost its "Costs ACTION COMPLETE" but the facet
            // browse kept it).
            if (!isClock && col == 1)
            {
                string costs = RequiresCell(actions[row], out bool isStatus);
                if (isStatus) return costs;
                return Lex.T(keys[col]) + ": " + (costs ?? Lex.T("loc.none"));
            }
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
            if (outcomes != null && outcomes.gameObject.activeInHierarchy
                && Util.RenderedUp(outcomes))
            {
                foreach (var tmp in outcomes.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (!Util.RenderedUp(tmp.transform, outcomes)) continue;
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

        /// <summary>The card's rendered name. The End Cycle card draws its label on
        /// its BUTTON, not in an "Action Name" element, so admitting it to this
        /// table made the row speak the raw object name — "End Cycle Action." where
        /// the screen says "END CYCLE" (ride 2026-08-05, my own regression). Read
        /// the button the way the rig's End Cycle row already does; the object name
        /// stays the last resort it always was.</summary>
        private static string ActionName(Transform card)
        {
            string drawn = Describe.TextUnder(card, "Action Name");
            if (drawn != null) return drawn;
            var button = FindDeep(card, "Dice Slot Button");
            if (button != null && button.gameObject.activeInHierarchy)
            {
                string label = Describe.FirstText(button.gameObject);
                if (!string.IsNullOrEmpty(label)) return label;
            }
            return card.name.TrimEnd();
        }

        /// <summary>Full read on row switch (CS1 ruling): every populated facet in
        /// column order, narrative last; disabled rows carry their reason.</summary>
        private static string ActionRow(Transform card)
        {
            var sb = new System.Text.StringBuilder(ActionName(card)).Append('.');
            string costs = RequiresCell(card, out bool costIsStatus);
            if (costs != null)
            {
                // A status sentence in the cost badge ("ACTION COMPLETE" on a
                // done card) speaks as-is — never under the Costs header (log
                // finding 2026-08-02: "Costs ACTION COMPLETE, ENDURE").
                if (costIsStatus)
                    sb.Append(' ').Append(costs).Append('.');
                else
                    sb.Append(' ').Append(Lex.T("loc.col.costs")).Append(' ')
                      .Append(costs).Append('.');
            }
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
                // Name facet = the name alone (log finding 2026-08-02: the
                // "Name:" cell spoke the entire card).
                case 0: return ActionName(card);
                case 1: return RequiresCell(card);
                case 2: return RiskCell(card);
                case 3: return CostCell(card);
                case 4: return PredictedCell(card);
                default: return Describe.TextUnder(card, "Description");
            }
        }

        /// <summary>The merged requirements cell (CS1 ruling, and CS1's blanket
        /// "takes a die" bug NOT re-imported — owner catch, cantine ride: the
        /// Gamepad Dice Slot has TWO variants (D11): the dice picker slot AND the
        /// item-cost slot (Check Amount/Slot Item — cryo/item payment). The
        /// take-kind is the slot FSM's own state vocabulary, and a non-die cost
        /// speaks its RENDERED amount; FSM variables back it only when render
        /// fails, loudly (owner law: render is the source of truth, backing
        /// second). Then the rendered skill + modifier tier.</summary>
        private static string RequiresCell(Transform card)
            => RequiresCell(card, out _);

        private static string RequiresCell(Transform card, out bool isStatus)
        {
            isStatus = false;
            var parts = new List<string>();
            // THE requirement render (canteen ride capture 2026-07-31): the card
            // states its own requirement as a sentence — the Input Border's
            // Relationship line, category AND value, every card kind: dice cards
            // render "INPUT DICE", cryo cards "INPUT 13 CRYO". One render source
            // of truth; the COST element and the slot's cost variable are the
            // loudly-logged backing chain (they read 0 pre-engagement — the
            // ride's "Costs: 0").
            string take = null;
            var inputBorder = FindDeep(card, "Input Border");
            if (inputBorder != null)
            {
                string rel = Describe.TextUnder(inputBorder, "Relationship");
                if (rel != null)
                {
                    if (rel.StartsWith("INPUT ")) rel = rel.Substring(6).Trim();
                    take = rel.Equals("DICE", System.StringComparison.OrdinalIgnoreCase)
                        ? Lex.T("loc.takes-die")
                        : rel;
                }
            }
            if (take == null)
            {
                take = Describe.TextUnder(card, "COST");
                if (take == null)
                {
                    var slot = FindDeep(card, "Gamepad Dice Slot");
                    var slotFsm = slot != null ? slot.GetComponent<PlayMakerFSM>() : null;
                    if (slotFsm != null)
                    {
                        var costInt = slotFsm.FsmVariables.GetFsmInt("Item Cost");
                        var costFloat = slotFsm.FsmVariables.GetFsmFloat("Item Cost");
                        if (costInt != null) take = costInt.Value.ToString();
                        else if (costFloat != null) take = costFloat.Value.ToString("0.#");
                    }
                }
                if (take != null)
                    LogOnce("[Location] no Input Border relationship on " + ActionName(card)
                        + " — backing lane spoken, capture needed");
            }
            if (take != null) parts.Add(take);

            // Status sentences ("ACTION COMPLETE") stand alone — the skill
            // chip beside a done card is stale chrome, not a cost part. Every
            // trip logs once: a digitless REAL cost misclassified here would
            // otherwise vanish silently (sync pass LOW-9).
            if (take != null && CostIsStatus(take))
            {
                LogOnce("[Location] cost badge read as STATUS: \"" + take + "\"");
                isStatus = true;
                return take;
            }

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

        /// <summary>A cost text that is really a STATUS sentence: no digit and
        /// not the die word — the badge repurposed as state ("ACTION
        /// COMPLETE"). Real costs render digits ("13 CRYO") or DICE.</summary>
        private static bool CostIsStatus(string take)
        {
            if (string.IsNullOrEmpty(take)) return false;
            if (take == Lex.T("loc.takes-die")) return false;
            foreach (char c in take)
                if (char.IsDigit(c)) return false;
            return true;
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

        /// <summary>The PREDICTIVE perk block — perk-gated in data (INTUIT_PERKS,
        /// D15) and render-gated on screen. ALPHA-honest (ride V1: the block sits
        /// active-but-alpha-hidden without the perk — object presence is not screen
        /// presence): only effectively-visible text speaks.</summary>
        private static string PredictedCell(Transform card)
        {
            var block = FindDeep(card, "PREDICTIVE");
            if (block == null || !block.gameObject.activeInHierarchy) return null;
            if (!Util.RenderedUp(block)) return null;
            var parts = new List<string>();
            foreach (var tmp in block.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform, block)) continue;
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

        /// <summary>Name-and-progress fused cell (owner ruling): "Name, x of y."</summary>
        private static string ClockNameCell(Transform clock)
        {
            string progress = ClockProgress(clock);
            return ClockName(clock) + (progress != null ? ", " + progress : "");
        }

        private static string ClockRow(Transform clock)
        {
            string narrative = ClockNarrative(clock);
            return ClockNameCell(clock) + "." + (narrative != null ? " " + narrative : "");
        }

        private static string ClockCell(Transform clock, int col)
        {
            switch (col)
            {
                case 0: return ClockNameCell(clock);
                default: return ClockNarrative(clock);
            }
        }

        /// <summary>Progress via the shared clock-class query (render-read UICircle,
        /// owner ruling ride V3).</summary>
        private static string ClockProgress(Transform clock)
            => GameQueries.ClockProgress(GameQueries.ClockFsm(clock));

        // ---------- Outcome pipeline hooks (DiceFlow's resolution reader) ----------

        /// <summary>Commit-time clock snapshot (CS1 SnapshotForDiff idiom): the open
        /// group's clock rows and their rendered progress.</summary>
        internal static Dictionary<Transform, string> ClockSnapshot()
        {
            var snapshot = new Dictionary<Transform, string>();
            _builtAt = -1f; // fresh rows
            foreach (var clock in Clocks())
                snapshot[clock] = ClockProgress(clock) ?? "";
            return snapshot;
        }

        /// <summary>Post-outcome clock changes vs a commit-time snapshot: one
        /// "name, x of y" line per clock whose rendered progress moved (new clocks
        /// included — a resolution can reveal one).</summary>
        internal static List<string> ClockChanges(Dictionary<Transform, string> snapshot)
        {
            var changes = new List<string>();
            _builtAt = -1f; // fresh rows
            foreach (var clock in Clocks())
            {
                string now = ClockProgress(clock) ?? "";
                if (now.Length == 0) continue;
                if (snapshot == null || !snapshot.TryGetValue(clock, out string was) || was != now)
                    changes.Add(ClockName(clock) + ", " + now + ".");
            }
            return changes;
        }

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
