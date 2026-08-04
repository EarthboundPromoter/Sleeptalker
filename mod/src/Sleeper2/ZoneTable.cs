using System;
using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The station zone table — the general node-table binding on the
    /// TableEngine (owner design 2026-07-26; columns to the approved spec
    /// 2026-07-26/31, decodes D1/D2): rows are StationAtlas nodes, columns are the
    /// CS1 map-table grammar re-derived for CS2 — Name (flags ride it) | Clock |
    /// Drives | Actions | Description. Row arrival drives the highlight (hover,
    /// engine convention), row speech is the compressed row report from the
    /// rendered billboard, Left/Right browse facets with stable geometry (empty
    /// cells speak terse facet-specific emptiness, never skip — CS1 ruling 3),
    /// Space is the full stable-geometry report, Enter commits from ANY cell
    /// (CS1 ruling 9 — horizontal position is facet browsing).
    ///
    /// Architecture per the CS1 "build this flexibly" ruling: a column registry of
    /// {header, cell provider}; the row model is atlas DATA — strings compose only
    /// at announce time, in this file's wording (Lex-keyed).
    ///
    /// Coexists with the game's native WASD camera walk: manual panning speaks
    /// through the focus path, table walking speaks table-side and suppresses the
    /// selector's echo for a beat (the CS1 map-table suppression).</summary>
    internal static class ZoneTable
    {
        private const float CacheWindow = 0.4f;

        private static List<StationAtlas.Node> _nodes = new List<StationAtlas.Node>();
        private static float _builtAt = -1f;
        private static bool _entered;

        /// <summary>Focus-echo suppression window for table-driven camera moves.</summary>
        private static float _driveEchoUntil = -1f;
        public static bool SuppressLocationFocus(GameObject go)
            => Time.unscaledTime < _driveEchoUntil && go != null && go.name == "Location Button";

        // ---------- Columns (registry per the CS1 flexibility ruling) ----------

        private sealed class Column
        {
            public string HeaderKey;                       // Lex key for the spoken header
            public Func<StationAtlas.Node, string> Cell;   // content; null/empty = empty cell
            public string EmptyKey;                        // terse facet-specific emptiness
        }

        private static readonly Column[] Columns =
        {
            new Column { HeaderKey = "zone.col.name",
                Cell = n => NameCell(StationAtlas.ReadNameFacet(n)),
                EmptyKey = "zone.empty" },
            new Column { HeaderKey = "zone.col.clock", Cell = ClockCell,
                EmptyKey = "zone.clock.none" },
            new Column { HeaderKey = "zone.col.drives", Cell = DrivesCell,
                EmptyKey = "zone.drives.none" },
            new Column { HeaderKey = "zone.col.actions", Cell = ActionsCell,
                EmptyKey = "zone.actions.none" },
            new Column { HeaderKey = "zone.col.description",
                Cell = StationAtlas.ReadDescription,
                EmptyKey = "zone.desc.none" },
        };

        /// <summary>The row's live column set (owner rulings 2026-07-31 and
        /// 2026-08-01, explicit amendments of stable geometry for these facets):
        /// the Clock cell EXISTS only when the row has a rendered clock — never
        /// "Clock: none" — and the Drives cell EXISTS only when the location has
        /// an active drive pip, speaking "Tracked drive: name" (tracking stays
        /// the game's own gate; presentation ruling of 2026-08-01).</summary>
        private static List<Column> ColumnsFor(StationAtlas.Node node)
        {
            var cols = new List<Column>(Columns.Length);
            var scratch = new List<string>();
            foreach (var column in Columns)
            {
                if (column.HeaderKey == "zone.col.clock"
                    && !StationAtlas.ReadClock(node, scratch)) continue;
                if (column.HeaderKey == "zone.col.drives"
                    && StationAtlas.ReadDrives(node).Count == 0) continue;
                cols.Add(column);
            }
            return cols;
        }

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Nodes().Count + Ops().Count,
            Cols = row => row < Nodes().Count
                ? ColumnsFor(Nodes()[row]).Count : 1,
            // The zone row read is the PRIMARY node read on this surface —
            // a census callout held from away composes ahead of it (owner
            // ruling 2026-08-04; delta-pass HIGH: without this seam the
            // backstop order-inversion would have been the normal path).
            RowSpeech = (r, c) => WithCensusPrefix(RowArriveSpeech(r, c)),
            CellSpeech = (r, c) => CellRead(r, c),
            // Rig side is a stacked grid (owner design 2026-07-31): Rooms over
            // Ship operations (End Cycle + crew assignment — the RIG display's
            // rendered buttons; not nodes, still table nav).
            SectionOf = row => row < Nodes().Count ? 0 : 1,
            SectionPrefix = s => Lex.T(s == 1 ? "zone.section.ops" : "zone.section.rooms"),
            EmptyRow = () => Lex.T("zone.empty"),
            EmptyCol = () => Lex.T("zone.empty"),
            EmptyDetail = () => Lex.T("zone.empty"),
            EmptyCommit = () => Lex.T("zone.empty"),
            Source = "zone",
        };

        private static string WithCensusPrefix(string row)
        {
            if (string.IsNullOrEmpty(row)) return row;
            var callout = StationCensus.ComposePrefix();
            return callout == null ? row : callout + " " + row;
        }

        public static void Init()
        {
            // Crew task dial (owner ruling 2026-08-03 — the selection was
            // unvoiced): "+ FUEL"/"+ SUPPLIES"/"+ CRYO" are the dial's
            // resting selection states (live FSM capture of record); entry
            // is the change moment. Announce only a CHANGED selection — rig
            // re-activation replays the resting state.
            foreach (var state in new[] { "+ FUEL", "+ SUPPLIES", "+ CRYO" })
                FsmSignals.Subscribe(null, state, OnTaskDial);
            Table.OnRowArrive = (prev, row) =>
            {
                var nodes = Nodes();
                if (row < 0 || row >= nodes.Count) return; // ops rows: no hover
                var button = nodes[row].Button;
                if (button == null) return;
                // Engine convention, refined (owner ruling 2026-07-26): node
                // focus is the HIGHLIGHT path, not selection — selection runs
                // the authored camera move (the location zoom); highlight is
                // the follow method. Row arrival hovers the node's button (and
                // its Clicker child, the event surface) exactly as the game's
                // own pointer machinery would; the previous node is un-hovered
                // first. The focus echo stays suppressed; the table spoke.
                // Facet reads run after this hover, so the billboard is
                // rendered — the load-bearing invariant (sync HIGH-3: the
                // hover-when-aligned variant broke reads on exactly the
                // sticky rows; the nameplate question is an OPEN owner
                // ruling, see checklist M12).
                _driveEchoUntil = Time.unscaledTime + 1.5f;
                HoverNode(_hovered, true);
                HoverNode(button, false);
                _hovered = button;
            };
            Table.Detail = (row, col) => Table.Say(FullReport(row));
            // Camera follow (D17 rebuild, 2026-08-01 — the CS1 shape proper):
            // ONE write of the wire accumulator to the closed-form target, only
            // when the row ACTUALLY changed, post-speech; the game's own
            // SmoothDamp glides the pan. The closed-loop stepping drive is
            // deleted. Pan targets come ONLY from rendered table rows (owner
            // requirement): this hook is the sole caller and nodes[row] is the
            // whitelisted, render-gated row set — the D17 dataset is a
            // conversion aid, never a target source.
            Table.AfterRowSpeech = (prev, row) =>
            {
                if (row == prev) return;
                var nodes = Nodes();
                if (row < 0 || row >= nodes.Count) { _verifyTarget = null; return; }
                PanTo(nodes[row]);
            };

            Table.Commit = (row, col) =>
            {
                var nodes = Nodes();
                if (row >= nodes.Count)
                {
                    var ops = Ops();
                    int oi = row - nodes.Count;
                    if (oi < 0 || oi >= ops.Count) return;
                    if (ops[oi].Target != null) Navigator.Click(ops[oi].Target);
                    else Table.Say(ops[oi].Speech()); // unclickable renders repeat
                    return;
                }
                if (row < 0) return;
                var button = nodes[row].Button;
                if (button == null) return;
                // COMMIT GATE (owner build 2026-08-04, REBUILT same day on
                // the owner's correction — "selection is not in doubt").
                // The first build gated on the proximity selector's own
                // "Closest UI Button" agreeing with our row. That was wrong
                // twice: (1) the variable is the selector's WORKING value
                // and the selector rests in Idle (event-driven, not
                // per-frame), so it is stale by construction — live capture
                // of record: it read Gaia's Gyre while the player was on
                // SPINDLE TRADE HALL, refusing a perfectly good commit
                // eight times; (2) the mod commits down the POINTER path
                // (row hover raises MouseOn, then the click) — the game's
                // own mouse channel, which never required the proximity
                // selector to agree. The one genuine dead-press cause is
                // the button refusing activation, which is a single
                // unambiguous flag the game's own click path reads too.
                // So: no selection opinion at all — ask the button.
                var refusal = CommitRefusal(button);
                if (refusal != null)
                {
                    Table.Say(Lex.T(refusal));
                    Plugin.Log.LogWarning("[Zone] commit refused (" + refusal
                        + ") at \"" + nodes[row].Name + "\" — capture");
                    return;
                }
                _driveEchoUntil = Time.unscaledTime + 1.5f;
                Navigator.Click(button);
            };
        }

        /// <summary>The table owns arrows/Enter on the zone floors — Station and
        /// RigRooms (rig rooms ARE zone nodes, D9). One authority decides
        /// everything above (ModeModel; the ride-V1 scattered-gate era is over).</summary>
        public static bool Active()
        {
            var mode = ModeModel.Current();
            if (mode != Mode.Station && mode != Mode.RigRooms) return false;
            if (TopBarTable.Entered) return false; // V-table excursion owns the keys
            return Nodes().Count + Ops().Count > 0;
        }

        public static bool HandleKeys()
        {
            if (!_entered)
            {
                // First entry: start the walk where the camera already is — the
                // selector's current pick, the game's own truth.
                _entered = true;
                _enteredRigSide = GameQueries.RigSide();
                Table.Reset(RowOfClosest());
            }
            return Table.HandleKeys();
        }

        private static bool _enteredRigSide;

        /// <summary>Suspension vs exit (sync review HIGH-2; CS1 D3 — overlays are
        /// EXCURSIONS): pause, tutorial, dialogue, dice, map, the V-table and the
        /// action view all suspend — cursor and hover kept, silent return. The
        /// table exits (reset, un-hover) only on a GENUINE surface change: the
        /// zone flips sides (Station↔RigRooms — two different node lists) or the
        /// scene goes away.</summary>
        public static void Tick()
        {
            VerifyTick();
            DescWatchTick();
            TaskAnnounceTick();
            if (!_entered) return;
            var mode = ModeModel.Current();
            bool onZoneFloor = mode == Mode.Station || mode == Mode.RigRooms;
            if (onZoneFloor && GameQueries.RigSide() != _enteredRigSide)
            {
                ExitTable();
                return;
            }
            if (mode == Mode.Title || mode == Mode.Travel)
                ExitTable();
        }

        /// <summary>Scene teardown: full exit (containers are gone).</summary>
        public static void OnSceneChanged() => ExitTable();

        // ---------- Camera follow (D17 single-write + settle verification) ----------

        private static StationAtlas.Node _verifyTarget;
        private static bool _verifyOutOfBand;
        private static float _verifyValue;
        private static float _verifyAt = -1f;
        private static float _verifyDeadline = -1f;
        private static readonly HashSet<string> _scatterLogged = new HashSet<string>();

        /// <summary>Why the game would refuse this button, as a Lex key, or
        /// null when it will accept activation. Mirrors Navigator.Click's OWN
        /// refusal test exactly — the first cut asked only IsInteractable(),
        /// a strict SUBSET (it ignores object activity), so an inactive
        /// button passed the gate and then died mute inside the click path:
        /// 42 raw refusals at Wellspring against zero spoken reasons (owner
        /// bug 2026-08-04). An undrawn node is reported as such: its whole
        /// Location Contents subtree is deactivated while the camera is
        /// elsewhere, so "off screen" is the render-honest reading, not a
        /// guess about selection.</summary>
        private static string CommitRefusal(GameObject button)
        {
            if (button == null) return "zone.commit.unselectable";
            if (!button.activeInHierarchy) return "zone.commit.offscreen";
            var sel = button.GetComponent<UnityEngine.UI.Selectable>();
            if (sel == null) return null;
            if (!sel.isActiveAndEnabled || !sel.IsInteractable())
                return "zone.commit.unselectable";
            return null;
        }

        // (AlignmentTick retired 2026-08-04 with the selection-proxy gate:
        // the commit is a synchronous question — the button either accepts
        // activation or it does not — so there is nothing to await.)

        /// <summary>One wire write per actual row change (D17 recipe). The
        /// settle verify judges the WIRE, not the selector (ride V4
        /// recalibration, owner-validated camera vs constant selector noise):
        /// the transfer function's promise is that the damper converges on the
        /// written value. Selector disagreement is the lateral-scatter class —
        /// D17 live correction 4 — logged once per node as info, never fought.</summary>
        private static void PanTo(StationAtlas.Node node)
        {
            _verifyTarget = null;
            if (node.Button == null || node.Root == null) return;
            // Anchor = the node ROOT (ride V4 live finding, 2026-08-01): the
            // Location Button is billboard-DISPLACED at runtime (camera-facing
            // tilt, ~400 z-units at Hexport) — panning to it lands one node
            // short. The root is the stable authored transform, and it is what
            // D17's per-node predictions verify against (Docks −1297 ✓ live).
            if (!GameQueries.CameraPanTo(node.Root.position,
                out bool outOfBand, out float written)) return;
            _verifyTarget = node;
            _verifyOutOfBand = outOfBand;
            _verifyValue = written;
            // Polling judge (owner build 2026-08-04; the 0.8s single sample
            // read the SmoothDamp tail as 40 false mismatches — deltas were
            // pair-symmetric and distance-proportional, the damper still
            // converging, worst on pans long enough to hit the velocity
            // cap): poll from 0.3s, mismatch only if never converged by the
            // deadline.
            _verifyAt = Time.unscaledTime + 0.3f;
            _verifyDeadline = Time.unscaledTime + 2.5f;
        }

        private static void VerifyTick()
        {
            if (_verifyTarget == null || Time.unscaledTime < _verifyAt) return;
            var mode = ModeModel.Current();
            if (mode != Mode.Station && mode != Mode.RigRooms)
            {
                _verifyTarget = null;
                return;
            }
            if (!GameQueries.WireSettled(_verifyValue, out float actual))
            {
                if (Time.unscaledTime < _verifyDeadline) return; // still gliding
                var missed = _verifyTarget;
                _verifyTarget = null;
                // The real fail-loud seam: the damper never landed on our value
                // (absorbed write, unexpected clamp, something fighting the wire).
                Plugin.Log.LogWarning("[Zone] follow WIRE mismatch"
                    + (_verifyOutOfBand ? " (out-of-band, edge pan — class 2/3)" : "")
                    + ": wrote " + _verifyValue.ToString("0.0") + ", never converged ("
                    + actual.ToString("0.0") + " at deadline) for \"" + missed.Name
                    + "\" — capture");
                return;
            }
            var target = _verifyTarget;
            _verifyTarget = null;
            var closest = StationAtlas.ClosestButton();
            if (closest == target.Button) return;
            // Wire landed; the selector holds a neighbor — lateral scatter,
            // expected on far-off-line nodes. Info, once per node.
            if (_scatterLogged.Add(target.Name))
                Plugin.Log.LogInfo("[Zone] selector holds a neighbor at \""
                    + target.Name + "\" (lateral scatter, D17 LC-4"
                    + (_verifyOutOfBand ? "; out-of-band edge pan" : "") + ") — "
                    + (closest != null ? Util.PathOf(closest) : "nothing"));
        }

        private static void ExitTable()
        {
            _entered = false;
            _verifyTarget = null;
            Table.Reset();
            if (_hovered != null)
            {
                HoverNode(_hovered, true);
                _hovered = null;
            }
        }

        private static GameObject _hovered;

        private static void HoverNode(GameObject button, bool exit)
        {
            if (button == null) return;
            Navigator.Hover(button, exit);
            var clicker = button.transform.Find("Clicker");
            if (clicker != null) Navigator.Hover(clicker.gameObject, exit);
        }

        private static List<StationAtlas.Node> Nodes()
        {
            if (Time.unscaledTime - _builtAt > CacheWindow)
            {
                _nodes = StationAtlas.Build();
                _builtAt = Time.unscaledTime;
            }
            return _nodes;
        }

        // ---------- Ship operations rows (rig side; owner design 2026-07-31) ----------

        private sealed class OpRow
        {
            public System.Func<string> Speech;
            public GameObject Target;   // null = renders but not clickable now
        }

        private static readonly List<OpRow> _ops = new List<OpRow>();
        private static float _opsBuiltAt = -1f;

        /// <summary>The RIG display's rendered operation buttons — End Cycle (the
        /// designed end-cycle input, D4; the top bar's lookalike is a leave
        /// button) and the crew assignment buttons. Rendered-only, alpha-honest;
        /// empty off the rig side.</summary>
        private static List<OpRow> Ops()
        {
            if (Time.unscaledTime - _opsBuiltAt <= CacheWindow) return _ops;
            _opsBuiltAt = Time.unscaledTime;
            _ops.Clear();
            if (!GameQueries.RigSide()) return _ops;
            var display = GameObject.Find("Letterbox Canvas/RIG display");
            if (display == null) return _ops;

            var endCycle = display.transform.Find("End Cycle Action");
            if (endCycle != null && endCycle.gameObject.activeInHierarchy
                && Util.RenderedUp(endCycle))
            {
                var button = FindDeep(endCycle, "Dice Slot Button");
                _ops.Add(new OpRow
                {
                    Target = button != null && button.gameObject.activeInHierarchy
                        ? button.gameObject : null,
                    Speech = () => EndCycleSpeech(endCycle, button),
                });
            }

            // The display's clock cards (owner report: the visible rig clocks were
            // never rows). RENDERED ones only — the ON and OFF again Switches hide
            // undiscovered clocks (D9), and the render test sees that.
            var clocksHub = display.transform.Find("Clocks HUB");
            if (clocksHub != null && clocksHub.gameObject.activeInHierarchy)
            {
                foreach (Transform card in clocksHub)
                {
                    if (!Util.RenderedUp(card)) continue;
                    var clockFsm = GameQueries.ClockFsm(card);
                    if (clockFsm == null) continue;
                    var c = card;
                    _ops.Add(new OpRow
                    {
                        Target = null, // display-only, like location clock rows
                        Speech = () => ClockCardSpeech(c),
                    });
                }
            }

            var crew = display.transform.Find("Crew Assignment/Display");
            if (crew != null && crew.gameObject.activeInHierarchy)
            {
                foreach (var b in crew.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                {
                    var go = b.gameObject;
                    if (!Util.RenderedUp(go.transform)) continue;
                    _ops.Add(new OpRow
                    {
                        Target = go,
                        Speech = () => CrewTaskSpeech(go),
                    });
                }
            }
            return _ops;
        }

        // ---------- Crew task selection voice (owner ruling 2026-08-03:
        // speak the active choice persistently on focus, and what it offers
        // per the game's render — the selected row's gold EFFECT chip,
        // "+ SUPPLIES", screenshot + live hierarchy of record) ----------

        private static string _taskAnnouncedFor;   // last-known resting state
        private static bool _taskAnnouncePending;
        private static float _taskPendingSince;

        private static void OnTaskDial(PlayMakerFSM fsm, string state)
        {
            // The dial is the Crew Assignment system's FSM (mechanism key:
            // it carries the Crew Tally roll-call var; Find* — Get*
            // auto-creates, post-mortem 2026-08-03).
            if (fsm == null || fsm.FsmVariables.FindFsmFloat("Crew Tally") == null)
                return;
            if (_taskAnnouncedFor == null) { _taskAnnouncedFor = state; return; }
            if (state == _taskAnnouncedFor) return;
            _taskAnnouncedFor = state;
            _taskAnnouncePending = true;
            _taskPendingSince = Time.unscaledTime;
        }

        /// <summary>The chip renders a beat behind the dial (the SFX
        /// interlude) — the announce fires the frame the render lands, with
        /// a loud 2s backstop that speaks from the dial state itself.</summary>
        private static void TaskAnnounceTick()
        {
            if (!_taskAnnouncePending) return;
            var row = SelectedTaskRow(out string offer);
            if (row != null && offer != null)
            {
                _taskAnnouncePending = false;
                string label = Describe.FirstText(row) ?? row.name;
                SpeechService.Say(Lex.T("zone.active-task") + ": " + label
                    + ", " + offer + ".", Priority.Queued, "zone");
            }
            else if (Time.unscaledTime - _taskPendingSince > 2f)
            {
                _taskAnnouncePending = false;
                Plugin.Log.LogWarning(
                    "[Zone] task chip never rendered — dial-state announce");
                SpeechService.Say(Lex.T("zone.active-task") + ": "
                    + TranscodePlus(_taskAnnouncedFor) + ".",
                    Priority.Queued, "zone");
            }
        }

        /// <summary>Crew task row read: the selected task appends its state
        /// and offering — "SOURCE SUPPLIES button. Active task, plus
        /// SUPPLIES." — persistently, from the rendered chip.</summary>
        private static string CrewTaskSpeech(GameObject go)
        {
            string label = (Describe.FirstText(go) ?? go.name)
                + Lex.T("topbar.button-suffix");
            string offer = CrewTaskOffer(go.transform);
            return offer == null ? label
                : label + " " + Lex.T("zone.active-task") + ", " + offer + ".";
        }

        /// <summary>The selected task's rendered offering — the row's EFFECT
        /// chip child text, leading plus transcoded. Null = not the selected
        /// row (only the selection renders a chip).</summary>
        private static string CrewTaskOffer(Transform row)
        {
            foreach (Transform child in row)
            {
                if (!child.name.StartsWith("EFFECT")) continue;
                if (!child.gameObject.activeInHierarchy
                    || !Util.RenderedUp(child)) continue;
                foreach (var tmp in child.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    string text = SpeechService.Clean(tmp.text);
                    if (!string.IsNullOrEmpty(text)) return TranscodePlus(text);
                }
            }
            return null;
        }

        private static string TranscodePlus(string text)
            => text.StartsWith("+")
                ? Lex.T("glyph.plus") + " " + text.Substring(1).Trim()
                : text;

        private static GameObject SelectedTaskRow(out string offer)
        {
            offer = null;
            var display = GameObject.Find("Letterbox Canvas/RIG display");
            var crew = display != null
                ? display.transform.Find("Crew Assignment/Display") : null;
            if (crew == null || !crew.gameObject.activeInHierarchy) return null;
            foreach (var b in crew.GetComponentsInChildren<UnityEngine.UI.Button>(false))
            {
                string o = CrewTaskOffer(b.transform);
                if (o != null) { offer = o; return b.gameObject; }
            }
            return null;
        }

        /// <summary>End Cycle row: the button's rendered label, its own
        /// interactable state (CycleBlocked renders as non-interactable), and the
        /// active cost-strip line (Normal/Starving/Supplied variants — whichever
        /// renders).</summary>
        private static string EndCycleSpeech(Transform endCycle, Transform button)
        {
            string label = button != null
                ? (Describe.FirstText(button.gameObject) ?? endCycle.name) : endCycle.name;
            var sb = new System.Text.StringBuilder(label);
            var buttonUi = button != null
                ? button.GetComponent<UnityEngine.UI.Button>() : null;
            if (buttonUi != null && !buttonUi.IsInteractable())
                sb.Append(' ').Append(Lex.T("zone.disabled"));
            foreach (var tmp in endCycle.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (button != null && tmp.transform.IsChildOf(button)) continue;
                if (!Util.RenderedUp(tmp.transform, endCycle)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text)) continue;
                sb.Append(". ").Append(text);
                break; // the active cost-strip line
            }
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>Rig clock card row: "Name, x of y." + narrative (the fused
        /// cell ruling; progress from the drawn circle).</summary>
        private static string ClockCardSpeech(Transform card)
        {
            string name = Describe.TrimQuotes(Describe.TextUnder(card, "Clock Name"))
                ?? card.name;
            string progress = GameQueries.ClockProgress(GameQueries.ClockFsm(card));
            string narrative = Describe.TextUnder(card, "Clock Description");
            return name + (progress != null ? ", " + progress : "") + "."
                 + (narrative != null ? " " + narrative : "");
        }

        private static string OpSpeech(int row)
        {
            var ops = Ops();
            int oi = row - Nodes().Count;
            if (oi < 0 || oi >= ops.Count) return Lex.T("zone.empty");
            return ops[oi].Speech();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static int RowOfClosest()
        {
            var closest = StationAtlas.ClosestButton();
            if (closest == null) return 0;
            var nodes = Nodes();
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].Button == closest)
                    return i;
            return 0;
        }

        // ---------- Speech composition (all reads live-after-hover) ----------

        /// <summary>Row arrival: at the Name column the compressed row report
        /// (name + flags + non-empty facets — CS1 ruling 3: the row report carries
        /// the compression); at a held facet column, name + that cell.</summary>
        private static string RowArriveSpeech(int row, int col)
        {
            var nodes = Nodes();
            if (row >= nodes.Count) return OpSpeech(row);
            if (row < 0) return Lex.T("zone.empty");
            if (col <= 0)
            {
                var node = nodes[row];
                // Description render race (log finding 2026-08-02: ~8 rows
                // spoke truncated on arrival, complete on a re-read — the
                // billboard populates a beat after the hover). An empty
                // description arms a short follow-up watch that speaks it
                // alone the frame it draws; the arrival read stays instant.
                if (string.IsNullOrEmpty(StationAtlas.ReadDescription(node)))
                {
                    _descWatchButton = node.Button;
                    _descWatchDeadline = Time.unscaledTime + 1f;
                }
                else
                {
                    _descWatchButton = null;
                }
                return CompressedReport(node);
            }
            var facet = StationAtlas.ReadNameFacet(nodes[row]);
            return NameCell(facet) + " " + CellRead(row, col);
        }

        // The description follow-up watch (one row at a time, keyed by the
        // node's stable Button object — Node instances rebuild on the cache
        // window; a row change or table exit drops it, so only the row the
        // cursor still sits on may complete late).
        private static GameObject _descWatchButton;
        private static float _descWatchDeadline;

        private static void DescWatchTick()
        {
            if (_descWatchButton == null) return;
            if (!_entered) { _descWatchButton = null; return; }
            var nodes = Nodes();
            int row = Table.Row;
            if (row < 0 || row >= nodes.Count || nodes[row].Button != _descWatchButton)
            {
                _descWatchButton = null;
                return;
            }
            string desc = StationAtlas.ReadDescription(nodes[row]);
            if (!string.IsNullOrEmpty(desc))
            {
                _descWatchButton = null;
                SpeechService.Say(desc + ".", Priority.Queued, "zone");
                return;
            }
            if (Time.unscaledTime >= _descWatchDeadline)
            {
                // Honest-empty: some nodes simply render no description.
                _descWatchButton = null;
            }
        }

        /// <summary>Facet browse: header-labeled cell, stable geometry — an empty
        /// facet speaks its terse emptiness, never skips (CS1 ruling 3).</summary>
        private static string CellRead(int row, int col)
        {
            var nodes = Nodes();
            if (row >= nodes.Count) return OpSpeech(row);
            if (row < 0) return Lex.T("zone.empty");
            var cols = ColumnsFor(nodes[row]);
            if (col < 0) col = 0;
            if (col >= cols.Count) col = cols.Count - 1; // row without the parked facet
            var column = cols[col];
            string content = column.Cell(nodes[row]);
            if (string.IsNullOrEmpty(content)) content = Lex.T(column.EmptyKey);
            return Lex.T(column.HeaderKey) + ": " + content;
        }

        /// <summary>Space: the full stable-geometry report — every facet, headered,
        /// empty forms included.</summary>
        private static string FullReport(int row)
        {
            var nodes = Nodes();
            if (row >= nodes.Count) return OpSpeech(row);
            if (row < 0) return Lex.T("zone.empty");
            var sb = new System.Text.StringBuilder();
            int count = ColumnsFor(nodes[row]).Count;
            for (int c = 0; c < count; c++)
                sb.Append(CellRead(row, c)).Append(' ');
            return sb.ToString().TrimEnd();
        }

        /// <summary>The compressed row report: name + flags, then only the facets
        /// that have content, unheadered description last (the CS1 map row idiom).</summary>
        private static string CompressedReport(StationAtlas.Node node)
        {
            var sb = new System.Text.StringBuilder(NameCell(StationAtlas.ReadNameFacet(node)));
            var clockTexts = new List<string>();
            if (StationAtlas.ReadClock(node, clockTexts))
                sb.Append(' ').Append(Lex.T("zone.col.clock")).Append(": ")
                  .Append(clockTexts.Count > 0
                      ? string.Join(", ", clockTexts.ToArray())
                      : Lex.T("zone.clock.shown")).Append('.');
            var drives = StationAtlas.ReadDrives(node);
            if (drives.Count > 0)
                sb.Append(' ').Append(Lex.T("zone.col.drives")).Append(": ")
                  .Append(string.Join(", ", drives.ToArray())).Append('.');
            var actions = StationAtlas.ReadActions(node);
            if (actions.Cards > 0)
                sb.Append(' ').Append(ActionsPhrase(actions));
            string desc = StationAtlas.ReadDescription(node);
            if (!string.IsNullOrEmpty(desc)) sb.Append(' ').Append(desc).Append('.');
            return sb.ToString();
        }

        private static string NameCell(StationAtlas.NameFacet facet)
        {
            var sb = new System.Text.StringBuilder(facet.Name).Append('.');
            if (facet.IsNew) sb.Append(' ').Append(Lex.T("zone.new"));
            if (facet.Disabled) sb.Append(' ').Append(Lex.T("zone.disabled"));
            return sb.ToString();
        }

        private static string ClockCell(StationAtlas.Node node)
        {
            var texts = new List<string>();
            if (!StationAtlas.ReadClock(node, texts)) return null;
            return texts.Count > 0
                ? string.Join(", ", texts.ToArray())
                : Lex.T("zone.clock.shown");
        }

        private static string DrivesCell(StationAtlas.Node node)
        {
            var drives = StationAtlas.ReadDrives(node);
            return drives.Count > 0 ? string.Join(", ", drives.ToArray()) : null;
        }

        private static string ActionsCell(StationAtlas.Node node)
        {
            var actions = StationAtlas.ReadActions(node);
            return actions.Cards > 0 ? ActionsPhrase(actions) : null;
        }

        private static string ActionsPhrase(StationAtlas.ActionsFacet actions)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(actions.Cards).Append(' ')
              .Append(Lex.T(actions.Cards == 1 ? "zone.actions.one" : "zone.actions.many"));
            if (actions.Unavailable > 0)
                sb.Append(", ").Append(actions.Unavailable)
                  .Append(' ').Append(Lex.T("zone.actions.unavailable"));
            sb.Append('.');
            return sb.ToString();
        }
    }
}
