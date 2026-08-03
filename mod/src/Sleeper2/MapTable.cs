using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The Map Screen table — the zone-table binding with the three map
    /// deltas (owner spec, build-plan.md, closed 2026-08-01; decode D8):
    ///
    /// Provider: the ACTIVE plane only. Belt = Zoomed Out Map UI (hub markers,
    /// rendered only in belt view); local = the one raised "&lt;sector&gt; Contracts"
    /// container (rendered AND interactable — the Scroll View FSM guarantees
    /// exactly one). Planes swap by render; Util.RenderedUp discriminates.
    ///
    /// Follow: plain uGUI selection of the marker's Location Button — the map's
    /// own follow (the scroll view chases selection, markers self-mirror it).
    /// NO camera-var writes anywhere on this surface; the station Focus rig is
    /// suspended for the whole map session by the game itself (D8).
    ///
    /// Columns (owner-ruled): Name (New Shine + you-are-here ride it; on
    /// card-bearing contract rows the marker subhead rides it too — ruling
    /// 2026-08-01) | Clock (EXISTENCE-BASED per the 2026-07-31 amendment) |
    /// Drives (D1 pips) | Fuel (added 2026-08-01 on arrived render evidence) |
    /// Risk | Description | Requirement | Payment | Client — the last five from
    /// the Sector Display's per-contract details card (owner ruling 2026-08-01),
    /// all existence-based on what the card renders. NO Actions column (markers
    /// carry no action groups).
    ///
    /// Sections: belt view groups markers by sector in the map's own
    /// left-to-right geometry ("&lt;sector&gt; sector." on crossing); local view is
    /// single-section. The Belt Button rides as an ops-style row (owner ruling
    /// 2026-08-01): the row exists whenever the button renders — including
    /// gated-dead — and speaks the button's rendered label (the CSUI_BELT
    /// family carries the game's own unavailable/blocked language); Enter is
    /// the native click, and a gated click stays dead natively. No mod-side
    /// gating logic.
    ///
    /// Enter on a marker = the native click into the travel pipeline (fuel
    /// check, hazard gating, Travel Confirm / crew selection). Those windows
    /// are native forced-focus dialogs: the table stands down while one
    /// renders (suspension via the Active gate), the window's DRAWN text is
    /// announced once at open (owner ruling 2026-08-01 — read what the window
    /// definitively draws, buttons excluded; focus reads carry those) and
    /// Backspace routes through GameQueries.MapBack's window-first ownership.</summary>
    internal static class MapTable
    {
        private const float CacheWindow = 0.4f;

        // ---------- Row model (built per plane; nodes + the Belt Button row) ----------

        private sealed class RowModel
        {
            public StationAtlas.Node Node;  // null = the Belt Button ops row
            public int Section;
            public GameObject OpTarget;
            public Func<string> OpSpeech;
        }

        private sealed class Group
        {
            public string Sector;
            public readonly List<StationAtlas.Node> Nodes = new List<StationAtlas.Node>();
            public float X;
        }

        private static List<RowModel> _rows = new List<RowModel>();
        private static List<string> _sections = new List<string>();
        private static float _builtAt = -1f;
        private static Transform _rowsPlane;
        private static bool _entered;
        private static Transform _lastPlane;

        /// <summary>Focus-echo suppression for table-driven marker selection (the
        /// zone-table suppression, map flavor): the table already spoke the row —
        /// the uGUI selection it sets is the follow, not news. Scoped to the OPEN
        /// map (sync pass F4): station markers share the "Location Button" name,
        /// and the root Reset's RefocusUI re-pick on close must speak.</summary>
        private static float _selectEchoUntil = -1f;
        public static bool SuppressMarkerFocus(GameObject go)
            => Time.unscaledTime < _selectEchoUntil && go != null
               && go.name == "Location Button" && GameQueries.MapOpen();

        // ---------- Columns (zone grammar minus Actions; owner spec) ----------

        private sealed class Column
        {
            public string HeaderKey;
            public Func<StationAtlas.Node, string> Cell;
            public string EmptyKey;
            // Live-rendered header (the card's own "REQUIREMENT //" / "PAYMENT //"
            // targets, slash-transcoded); null or a null return falls to HeaderKey.
            public Func<StationAtlas.Node, string> Header;
        }

        private static readonly Column[] Columns =
        {
            new Column { HeaderKey = "zone.col.name",
                Cell = n => NameCell(n), EmptyKey = "map.empty" },
            new Column { HeaderKey = "map.col.noroute",
                Cell = NoRouteCell, EmptyKey = "zone.empty" },
            new Column { HeaderKey = "zone.col.clock",
                Cell = ClockCell, EmptyKey = "zone.clock.none" },
            new Column { HeaderKey = "zone.col.drives",
                Cell = DrivesCell, EmptyKey = "zone.drives.none" },
            new Column { HeaderKey = "map.col.fuel",
                Cell = FuelCell, EmptyKey = "zone.empty" },
            new Column { HeaderKey = "loc.col.risk",
                Cell = RiskCell, EmptyKey = "zone.empty" },
            new Column { HeaderKey = "zone.col.description",
                Cell = DescriptionCell, EmptyKey = "zone.desc.none" },
            new Column { HeaderKey = "map.col.requirement",
                Cell = RequirementCell, EmptyKey = "zone.empty",
                Header = n => CardHeader(n, "Delivery") },
            new Column { HeaderKey = "map.col.payment",
                Cell = PaymentCell, EmptyKey = "zone.empty",
                Header = n => CardHeader(n, "Payment") },
            new Column { HeaderKey = "map.col.client",
                Cell = ClientCell, EmptyKey = "zone.empty" },
        };

        /// <summary>Existence-based cells: Clock (owner amendment 2026-07-31,
        /// carried from the zone table), Fuel (owner ruling 2026-08-01, added on
        /// the render evidence the v1 exclusion asked for), and the contract-card
        /// four — Risk / Requirement / Payment / Client (owner ruling 2026-08-01):
        /// each cell exists only when its fact renders/publishes. Description
        /// stays a standing column (card body on card rows, marker subhead
        /// otherwise — the subhead rides the Name cell on card rows).</summary>
        private static List<Column> ColumnsFor(StationAtlas.Node node)
        {
            var cols = new List<Column>(Columns.Length);
            var scratch = new List<string>();
            foreach (var column in Columns)
            {
                if (column.HeaderKey == "map.col.noroute"
                    && NoRouteCell(node) == null) continue;
                if (column.HeaderKey == "zone.col.clock"
                    && !StationAtlas.ReadClock(node, scratch)) continue;
                if (column.HeaderKey == "zone.col.drives"
                    && StationAtlas.ReadDrives(node).Count == 0) continue;
                if (column.HeaderKey == "map.col.fuel"
                    && FuelCostOf(node) == null) continue;
                if (column.HeaderKey == "loc.col.risk"
                    && RiskCell(node) == null) continue;
                if (column.HeaderKey == "map.col.requirement"
                    && RequirementCell(node) == null) continue;
                if (column.HeaderKey == "map.col.payment"
                    && PaymentCell(node) == null) continue;
                if (column.HeaderKey == "map.col.client"
                    && ClientCell(node) == null) continue;
                cols.Add(column);
            }
            // Belt sector rows: one column per contract the sector plate
            // draws (existence-based; numbered headers when several).
            int beltCards = BeltCards(node).Count;
            for (int i = 0; i < beltCards; i++)
            {
                int idx = i;
                cols.Add(new Column
                {
                    HeaderKey = "map.col.contract",
                    Cell = n => BeltCardCell(n, idx),
                    EmptyKey = "map.empty",
                    Header = beltCards > 1
                        ? (Func<StationAtlas.Node, string>)(n =>
                            Lex.T("map.col.contract") + " " + (idx + 1))
                        : null,
                });
            }
            return cols;
        }

        /// <summary>The marker's published fuel cost (D8 (e): the pipeline FSM's
        /// own Fuel Cost string / Fuel Cost Int — contract markers carry it from
        /// the localization table, hub/belt markers from the distance compute;
        /// the same value the Vector Display renders on select, which is the
        /// render evidence the Fuel column was gated on). Null = no cost
        /// published — the cell does not exist.</summary>
        private static string FuelCostOf(StationAtlas.Node node)
        {
            if (node.Button == null) return null;
            foreach (var fsm in node.Button.GetComponents<PlayMakerFSM>())
            {
                if (!HasStates(fsm, "Check Fuel", "Travel")) continue;
                var s = fsm.FsmVariables.GetFsmString("Fuel Cost");
                if (s != null && !string.IsNullOrEmpty(s.Value)) return s.Value.Trim();
                var i = fsm.FsmVariables.GetFsmInt("Fuel Cost Int");
                if (i != null && i.Value > 0) return i.Value.ToString();
            }
            return null;
        }

        private static string FuelCell(StationAtlas.Node node) => FuelCostOf(node);

        // ---------- Contract details card (owner ruling 2026-08-01) ----------
        // The Sector Display panel renders a full job card per active contract:
        // Name / Client / Type / body Description / REQUIREMENT / PAYMENT
        // (live-verified 2026-08-01). Those facts join the contract's row as
        // existence-based cells in the ruled order, read live at speech time;
        // the marker's own short Location Description (the subhead) moves into
        // the Name cell on card rows.

        /// <summary>The rendered details card for this marker, matched by
        /// rendered Name — both strings are game-rendered. Cards live under
        /// Sector Display/Contract Details/&lt;sector&gt; Contract Details (one
        /// container per sector; only the current one renders) as template
        /// clones carrying Name AND Description children — the shape is the
        /// discriminator, never the clone name (the sibling Contract Labels
        /// header block carries no Description). Null = no card renders for
        /// this marker (belt hubs; empty sectors).</summary>
        private static Transform ContractCard(StationAtlas.Node node)
        {
            var container = CardContainer();
            if (container == null) return null;
            string name = StationAtlas.ReadNameFacet(node).Name;
            if (string.IsNullOrEmpty(name)) return null;
            bool sawCard = false;
            foreach (Transform card in container)
            {
                if (!card.gameObject.activeInHierarchy || !Util.RenderedUp(card)) continue;
                if (card.Find("Description") == null) continue; // the Labels header block
                string cardName = CardText(card, "Name");
                if (cardName == null) continue;
                sawCard = true;
                if (string.Equals(cardName.Trim(), name.Trim(),
                    StringComparison.OrdinalIgnoreCase)) return card;
            }
            // A contract marker the panel renders no matching card for is a
            // broken guess — loud, but only on the contracts plane (belt hubs
            // are the expected miss).
            if (sawCard && _rowsPlane != null && _rowsPlane.name != "Zoomed Out Map UI")
                LogOnce("[Map] no details card matched marker \"" + name + "\"");
            return null;
        }

        /// <summary>The rendered contract lines of a BELT sector row (owner
        /// report 2026-08-03: the belt Sector Display draws the selected
        /// sector's active contracts as a compact list — name / client /
        /// risk — and the card-matching cells can never reach it: they match
        /// cards by the ROW'S rendered name, and no card is named after a
        /// sector). Belt plane only — the local plane speaks full cards on
        /// the contract rows themselves. The container must belong to this
        /// row's sector (the plate follows selection; a mismatch is the
        /// transition beat and reads as no cells).</summary>
        private static List<Transform> BeltCards(StationAtlas.Node node)
        {
            var cards = new List<Transform>();
            if (_rowsPlane == null || _rowsPlane.name != "Zoomed Out Map UI") return cards;
            var container = CardContainer();
            if (container == null) return cards;
            // Punctuation-blind match (sync pass HIGH-3: the marker is
            // "Holm's Rock", the container "Holms Rock Contract Details" —
            // a plain StartsWith never matches that sector).
            string sector = SectorOf(node);
            if (sector == null
                || !Canon(container.name).StartsWith(Canon(sector))) return cards;
            bool anyRendered = false;
            foreach (Transform card in container)
            {
                if (!card.gameObject.activeInHierarchy || !Util.RenderedUp(card)) continue;
                anyRendered = true;
                if (card.Find("Description") == null) continue; // the Labels header block
                if (CardText(card, "Name") == null) continue;   // nothing drawn to speak
                cards.Add(card);
            }
            if (anyRendered && cards.Count == 0)
                LogOnce("[Map] belt plate renders but no card shape matched (capture)");
            return cards;
        }

        /// <summary>One belt contract cell: exactly the card facts the plate
        /// draws, in its own left-to-right order (name, client, risk) —
        /// CardText is render-gated, so an undrawn fact never speaks.</summary>
        private static string BeltCardCell(StationAtlas.Node node, int index)
        {
            var cards = BeltCards(node);
            if (index >= cards.Count) return null;
            var parts = new List<string>(3);
            foreach (string child in new[] { "Name", "Client", "Type" })
            {
                string text = CardText(cards[index], child);
                if (text != null) parts.Add(text);
            }
            return parts.Count > 0 ? string.Join(". ", parts) + "." : null;
        }

        private static Transform CardContainer()
        {
            var root = GameQueries.MapRoot();
            var t = root != null ? root.Find("Sector Display/Contract Details") : null;
            if (t == null || !t.gameObject.activeInHierarchy) return null;
            foreach (Transform child in t)
                if (child.gameObject.activeInHierarchy && Util.RenderedUp(child))
                    return child; // one sector container renders at a time
            return null;
        }

        /// <summary>A card child's rendered text (RenderedUp-gated, cleaned).
        /// Null = absent or not drawn. The direct component wins so the
        /// Description body never reads its nested Payment/Delivery children.</summary>
        private static string CardText(Transform card, string path)
        {
            var t = card.Find(path);
            if (t == null) return null;
            var tmp = t.GetComponent<TMPro.TMP_Text>();
            if (tmp == null) tmp = t.GetComponentInChildren<TMPro.TMP_Text>(false);
            if (tmp == null || !Util.RenderedUp(tmp.transform)) return null;
            string text = SpeechService.Clean(tmp.text);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>Slash-run decoration off a rendered label ("REQUIREMENT //",
        /// "// HEXPORT") — visual styling, not content (the S3 transcode rule).</summary>
        private static string SlashLabel(string raw)
        {
            if (raw == null) return null;
            string s = raw.Trim().Trim('/').Trim();
            return s.Length > 0 ? s : null;
        }

        private static string RiskCell(StationAtlas.Node node)
        {
            var card = ContractCard(node);
            return card != null ? CardText(card, "Type") : null;
        }

        private static string ClientCell(StationAtlas.Node node)
        {
            var card = ContractCard(node);
            return card != null ? CardText(card, "Client") : null;
        }

        private static string RequirementCell(StationAtlas.Node node)
        {
            var card = ContractCard(node);
            return card != null ? CardText(card, "Description/Delivery/Delivery") : null;
        }

        private static string PaymentCell(StationAtlas.Node node)
        {
            var card = ContractCard(node);
            return card != null ? CardText(card, "Description/Payment/Payment") : null;
        }

        /// <summary>Requirement/Payment column headers speak the card's OWN
        /// rendered target labels, slash-transcoded ("REQUIREMENT //" →
        /// "REQUIREMENT"); a card without one falls to the Lex key, logged.</summary>
        private static string CardHeader(StationAtlas.Node node, string block)
        {
            var card = ContractCard(node);
            if (card == null) return null;
            string label = SlashLabel(CardText(card, "Description/" + block + "/Target"));
            if (label == null)
                LogOnce("[Map] card block " + block + " renders no Target label — Lex fallback");
            return label;
        }

        /// <summary>Description column: the card's body text on card rows (the
        /// subhead has moved to the Name cell there); the marker subhead
        /// otherwise. A card with no drawn body logs and leaves the cell empty —
        /// never repeats the subhead both places.</summary>
        private static string DescriptionCell(StationAtlas.Node node)
        {
            var card = ContractCard(node);
            if (card == null) return StationAtlas.ReadDescription(node);
            string body = CardText(card, "Description");
            if (body == null)
                LogOnce("[Map] details card renders no body text — empty Description cell");
            return body;
        }

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Rows().Count,
            Cols = row =>
            {
                var rows = Rows();
                return row >= 0 && row < rows.Count && rows[row].Node != null
                    ? ColumnsFor(rows[row].Node).Count : 1;
            },
            RowSpeech = (r, c) => RowArriveSpeech(r, c),
            CellSpeech = (r, c) => CellRead(r, c),
            SectionOf = row =>
            {
                var rows = Rows();
                return row >= 0 && row < rows.Count ? rows[row].Section : 0;
            },
            SectionPrefix = s => s >= 0 && s < _sections.Count ? _sections[s] : "",
            EmptyRow = () => Lex.T("map.empty"),
            EmptyCol = () => Lex.T("map.empty"),
            EmptyDetail = () => Lex.T("map.empty"),
            EmptyCommit = () => Lex.T("map.empty"),
            Source = "map",
        };

        public static void Init()
        {
            // Denial states exit within one PlayMaker update (sync pass
            // MED-6) — entry signals are the reliable catch; the poll in
            // BlockWatchTick stays as fallback.
            FsmSignals.Subscribe(null, "BLOCKED", (fsm, s) => OnDenialState(fsm, false));
            FsmSignals.Subscribe(null, "DEBRIS", (fsm, s) => OnDenialState(fsm, true));

            Table.OnRowArrive = (prev, row) =>
            {
                var rows = Rows();
                if (row < 0 || row >= rows.Count) return;
                var node = rows[row].Node;
                if (node == null) return;
                // Belt camera BEFORE selection: the scroll view never chases
                // selection (owner report 2026-08-03 — the CAUTION of record
                // proven out; details raised on an offscreen node, camera
                // parked). The pan puts the marker on screen first.
                PanBeltTo(node);
                if (node.Button == null || node.IsHere) return;
                if (!node.Button.activeInHierarchy) return;
                var es = EventSystem.current;
                if (es == null || es.currentSelectedGameObject == node.Button) return;
                // Selection raises the billboard (Selected behaves like hover,
                // so the billboard renders before the facet reads).
                _selectEchoUntil = Time.unscaledTime + 1.5f;
                es.SetSelectedGameObject(node.Button);
            };

            Table.Detail = (row, col) => Table.Say(FullReport(row));

            Table.Commit = (row, col) =>
            {
                var rows = Rows();
                if (row < 0 || row >= rows.Count) return;
                var r = rows[row];
                if (r.Node == null)
                {
                    // Belt Button row: native click; a gated (non-interactable)
                    // click speaks the element — the game's own label carries
                    // the unavailable/blocked language.
                    if (r.OpTarget != null) Navigator.Click(r.OpTarget);
                    else Table.Say(r.OpSpeech());
                    return;
                }
                if (r.Node.IsHere) { Table.Say(Lex.T("map.here")); return; }
                var button = r.Node.Button;
                if (button == null || !button.activeInHierarchy)
                { Table.Say(RowArriveSpeech(row, 0)); return; }
                // Native marker click → the travel pipeline whole (fuel check,
                // BLOCKED response, Travel Confirm, crew selection — D8 (d)).
                _selectEchoUntil = Time.unscaledTime + 1.5f;
                Navigator.Click(button);
                // BLOCKED watch (ride V4 owner report: denial sound with no
                // context): if this commit lands in the pipeline's BLOCKED
                // state, speak the game's own rendered reason (Vector Display).
                _blockWatch = button;
                _blockWatchUntil = Time.unscaledTime + 1.5f;
                _lastCommitButton = button; // travel-close discrimination
            };
        }

        /// <summary>The table owns the keys in Mode.Map — except while a native
        /// forced-focus sub-window renders (Travel Confirm / Crew / Leave
        /// Contract / blockers): those get native arrows + focus reads. And
        /// except while a travel commit is in flight (sync pass F2): between
        /// the confirm's Continue and the map's own Fade Up → Back, the root
        /// FSM still reads Open with no window up — a second Enter there would
        /// double-commit (double fuel, double cycle).</summary>
        public static bool Active()
        {
            if (ModeModel.Current() != Mode.Map) return false;
            if (GameQueries.MapSubWindowUp()) return false;
            if (TravelCommitInFlight()) return false;
            return true;
        }

        /// <summary>A map travel pipeline FSM (the Location Button class — it
        /// owns both Check Fuel and Travel states) sitting in the commit tail.
        /// Mechanism-class matched, never named. The render-first alternative
        /// (the screen Fader) pends its identity capture at ride.</summary>
        // The FULL decoded commit chain (D21 + sync pass HIGH-2 2026-08-03:
        // the old four-state list missed the 2.5s Wait and the whole TRN
        // chain — Enter stayed live mid-commit and TRN travels spoke a wrong
        // "Map closed" over their cutscene load).
        private static readonly HashSet<string> CommitTailStates = new HashSet<string>
        {
            "Travel Confirm?", "Wait", "TRN?",
            "Travel", "LOAD", "End Cycle", "Fade Up",
            "Travel 2", "LOAD 2", "End Cycle 2", "Fade Up 2",
        };

        private static bool TravelCommitInFlight()
        {
            var rows = Rows();
            for (int i = 0; i < rows.Count; i++)
            {
                var node = rows[i].Node;
                if (node == null || node.Button == null) continue;
                foreach (var fsm in node.Button.GetComponents<PlayMakerFSM>())
                {
                    if (!CommitTailStates.Contains(fsm.ActiveStateName)) continue;
                    if (!HasStates(fsm, "Check Fuel", "Travel")) continue;
                    return true;
                }
            }
            return false;
        }

        private static bool HasStates(PlayMakerFSM fsm, string a, string b)
        {
            var states = fsm.FsmStates;
            if (states == null) return false;
            bool hasA = false, hasB = false;
            foreach (var s in states)
            {
                if (s.Name == a) hasA = true;
                else if (s.Name == b) hasB = true;
            }
            return hasA && hasB;
        }

        public static bool HandleKeys()
        {
            if (!_entered)
            {
                // First entry: start on the marker the game already selected;
                // the invisible Gamepad Selection Button anchor parks at row 0.
                _entered = true;
                Table.Reset(RowOfSelected());
            }
            return Table.HandleKeys();
        }

        /// <summary>Watches the plane dial: the zoom flip (Belt Button, ours or
        /// native) is a genuine surface change — new marker population, announce
        /// + reset. Exit ONLY when the map itself closes (root FSM off Open) or
        /// the scene goes — overlay modes above Map (pause, tutorial,
        /// conversation, dice) are EXCURSIONS and merely suspend (sync pass F3;
        /// the excursion law): key ownership already stands down via Active,
        /// cursor and plane memory are kept for the silent return.</summary>
        public static void Tick()
        {
            if (!GameQueries.MapOpen())
            {
                if (_entered || _lastPlane != null)
                {
                    // Close announce (owner ruling 2026-08-02): name the floor
                    // the map closes TO — the hub by its OWN name (the sector
                    // label captured off the local plane), or the rig. A
                    // travel commit closes silently: the ride is the story.
                    bool travelClosing = TravelClosing();
                    string returnTo = GameQueries.RigSide()
                        ? Lex.T("mode.rigrooms")
                        : (_localSector ?? Lex.T("mode.station"));
                    Exit();
                    if (!travelClosing)
                        Table.Say(Lex.T("map.closed") + " " + returnTo + ".");
                }
                return;
            }
            BlockWatchTick();
            SubWindowWatchTick();
            VectorAuditTick();
            // Focus follows the game (owner design 2026-08-03): if the
            // reselector or mouse moves the game's selection to another
            // marker, the table row syncs SILENTLY — FocusPatch already
            // speaks the game-driven move. STRICT match only, and never
            // while the cursor stands on a buttonless row (sync pass HIGH-1:
            // ops and you-are-here rows leave selection parked on the last
            // marker, and the 0-fallback yanked the cursor there — Enter
            // then committed the wrong row; excursion selections match no
            // marker and must not re-anchor either).
            int selRow = RowOfSelectedStrict();
            if (selRow >= 0 && selRow != Table.Row && CurrentRowAnchored())
                Table.Reset(selRow);
            var plane = StationAtlas.ActiveMapPlane();
            if (plane == null) return;                    // the Trans beat between planes
            if (plane.name != "Zoomed Out Map UI") _localSector = SectorOfPlane(plane);
            if (_lastPlane == null)
            {
                // Opening plane (always local). "Map." announces the mode
                // (owner ruling 2026-08-02 — the open was silent).
                _lastPlane = plane;
                Table.Say(Lex.T("map.title"));
                return;
            }
            if (plane == _lastPlane) return;
            _lastPlane = plane;
            _builtAt = -1f;
            _entered = false;
            _lastPanState = null; // zoom flip: re-arm the belt camera write
            Table.Reset();
            Table.Say(PlaneLabel(plane));
        }

        // ---------- Belt camera (owner report 2026-08-03: table nav must
        // drive the camera like the hub table does — D17 doctrine, the game's
        // own machinery) ----------

        // The Scroll View FSM is the game's own pan machine (decode of
        // record: yaml 30181): "Get Location" self-routes to the SHIP's
        // sector at open (its events are useless for arbitrary targets), and
        // each sector state runs the authored EaseFloat pan on both scroll
        // axes PLUS that sector's detail activation batch. Entering the
        // state IS the native transit — targets, easing, and detail
        // animation all the game's own; we write once per row change.
        private const string ScrollViewPath =
            "Letterbox Canvas/Map Screen/Moving Map Zoomer (DOES NOT MOVE)/Scroll View";
        private static string _lastPanState;

        private static void PanBeltTo(StationAtlas.Node node)
        {
            var plane = StationAtlas.ActiveMapPlane();
            if (plane == null || plane.name != "Zoomed Out Map UI") return;
            string sector = SectorOf(node);
            if (string.IsNullOrEmpty(sector)) return;
            var go = GameObject.Find(ScrollViewPath);
            var fsm = go != null ? go.GetComponent<PlayMakerFSM>() : null;
            if (fsm == null)
            {
                LogOnce("[Map] Scroll View FSM missing — belt camera stays put");
                return;
            }
            // Sector label → state name, punctuation-blind ("Holm's Rock"
            // renders the apostrophe; the state is "Holms Rock"). Unmatched
            // sectors log loudly and stay put (story markers may have no
            // authored pan target).
            string target = null;
            string want = Canon(sector);
            foreach (var s in fsm.FsmStates)
                if (Canon(s.Name) == want) { target = s.Name; break; }
            if (target == null)
            {
                LogOnce("[Map] no scroll state for sector \"" + sector
                    + "\" — belt camera stays put");
                return;
            }
            // Write-once per row change (D17): never re-enter the state we
            // last wrote — replaying the activation batch buys nothing.
            if (target == _lastPanState || fsm.ActiveStateName == target)
            {
                _lastPanState = target;
                return;
            }
            _lastPanState = target;
            fsm.SetState(target);
        }

        private static string Canon(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        // The hub name the map opened over (sector of the local plane — the
        // map's own rendered label family); spoken on close.
        private static string _localSector;

        /// <summary>A travel commit is closing the map: the last-committed
        /// marker's pipeline FSM sits in its commit tail. Checked BEFORE Exit
        /// (the row caches are plane-keyed and already empty at close).</summary>
        private static GameObject _lastCommitButton;

        private static bool TravelClosing()
        {
            if (_lastCommitButton == null || !_lastCommitButton.activeInHierarchy) return false;
            foreach (var fsm in _lastCommitButton.GetComponents<PlayMakerFSM>())
            {
                if (!HasStates(fsm, "Check Fuel", "Travel")) continue;
                return CommitTailStates.Contains(fsm.ActiveStateName);
            }
            return false;
        }

        public static void OnSceneChanged() => Exit();

        private static void Exit()
        {
            _entered = false;
            _lastPlane = null;
            _builtAt = -1f;
            _selectEchoUntil = -1f; // the close-time RefocusUI re-pick must speak
            _blockWatch = null;
            _subWindow = null;
            _lastCommitButton = null;
            _localSector = null;
            _lastPanState = null; // reopen snaps to the ship; our write memory is stale
            Table.Reset();
        }

        // ---------- Sub-window announce (owner ruling 2026-08-01: read what
        // the window definitively draws — nothing else) ----------

        private static Transform _subWindow;

        /// <summary>Announce a native forced-focus dialog the moment it
        /// renders (Travel Confirm / Crew stages / blockers — the ride V5
        /// finding: focus reads carried only the buttons; the window body
        /// never spoke). A stage swap (Travel Confirm → Crew) announces the
        /// new window; the close is silent — the excursion law, the table
        /// resumes where it stood.</summary>
        private static void SubWindowWatchTick()
        {
            var window = GameQueries.MapSubWindow();
            if (window == _subWindow) return;
            _subWindow = window;
            if (window == null) return;
            string read = WindowDrawnText(window);
            // Owner ruling 2026-08-02: the crew confirm box renders only
            // "CONFIRM CREW" — the thing being confirmed (the loadout) stands
            // in the window beneath. Report it with the announce.
            if (window.name == "Crew Confrim")
            {
                string loadout = CrewLoadout();
                if (loadout != null)
                    read = (read != null ? read + " " : "") + loadout;
            }
            if (read != null) Table.Say(read);
            else LogOnce("[Map] sub-window \"" + window.name
                + "\" draws no non-button text — silent open");
        }

        /// <summary>The attached crew at confirm time: members whose Crew
        /// Window buttons rest equipped or locked into a slot, by rendered
        /// name, slot order. "No crew." when both slots are empty (the game
        /// renders its own Warning plate for that case in the window
        /// beneath). Name falls back to the button FSM's own crew-id string,
        /// logged.</summary>
        private static string CrewLoadout()
        {
            var root = GameQueries.MapRoot();
            var buttons = root != null ? root.Find("Crew Window/Crew Buttons") : null;
            if (buttons == null) return null;
            string slot1 = null, slot2 = null;
            foreach (Transform b in buttons)
            {
                if (!b.name.StartsWith("Crew Button")) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                var fsm = b.GetComponent<PlayMakerFSM>();
                string state = fsm != null ? fsm.ActiveStateName ?? "" : "";
                bool s1 = state == "Slot 1 Equip" || state == "Slot 1 LOCKED";
                bool s2 = state == "Slot 2 Equip" || state == "Slot 2 LOCKED";
                if (!s1 && !s2) continue;
                string name = null;
                foreach (var tmp in b.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (tmp.transform.name != "Name" && tmp.transform.parent != null
                        && tmp.transform.parent.name != "Name") continue;
                    if (!Util.RenderedUp(tmp.transform)) continue;
                    name = SpeechService.Clean(tmp.text);
                    if (name != null) break;
                }
                if (name == null)
                {
                    var id = fsm.FsmVariables.GetFsmString("Crew Member");
                    name = id != null && !string.IsNullOrEmpty(id.Value) ? id.Value : null;
                    if (name != null)
                        LogOnce("[Map] crew loadout: no rendered Name on \"" + b.name
                            + "\" — id string spoken");
                }
                if (name == null) continue;
                if (s1) slot1 = name; else slot2 = name;
            }
            if (slot1 == null && slot2 == null) return Lex.T("crew.none");
            var sb = new System.Text.StringBuilder(Lex.T("crew.loadout"));
            if (slot1 != null) sb.Append(' ').Append(slot1);
            if (slot2 != null)
            {
                if (slot1 != null) sb.Append(',');
                sb.Append(' ').Append(slot2);
            }
            return sb.Append('.').ToString();
        }

        /// <summary>Every line the window definitively draws EXCEPT button
        /// labels (native focus reads carry those): RenderedUp-gated per text
        /// at speech time, screen (hierarchy) order, deduped. The gate is the
        /// honesty story — alpha-parked strips (the Reduce Hunt lesson,
        /// 2026-08-01) never enter the utterance, and anything the game
        /// genuinely draws in future reads without a code change.</summary>
        private static string WindowDrawnText(Transform window)
        {
            var sb = new System.Text.StringBuilder();
            var seen = new HashSet<string>();
            foreach (var tmp in window.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                if (UnderSelectable(tmp.transform, window)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text) || !seen.Add(text)) continue;
                sb.Append(text);
                if (!text.EndsWith(".", StringComparison.Ordinal)) sb.Append('.');
                sb.Append(' ');
            }
            string result = sb.ToString().TrimEnd();
            return result.Length > 0 ? result : null;
        }

        private static bool UnderSelectable(Transform t, Transform stop)
        {
            for (var p = t; p != null && p != stop; p = p.parent)
                if (p.GetComponent<UnityEngine.UI.Selectable>() != null) return true;
            return false;
        }

        // ---------- BLOCKED response (D8 (d) step 3 — the silent denial) ----------
        // The fuel/hazard check has no window and no focus change: red tint, the
        // Inaccess sound, an animator flash on the Vector Display. The player
        // gets the game's own rendered reason: the display's readout (the fuel
        // cost the marker published on select), spoken once behind "Blocked."
        // — game-sanctioned word, rendered content, capture-before-teardown.

        private static GameObject _blockWatch;
        private static float _blockWatchUntil = -1f;

        private static void BlockWatchTick()
        {
            if (_blockWatch == null) return;
            if (Time.unscaledTime > _blockWatchUntil) { _blockWatch = null; return; }
            if (!_blockWatch.activeInHierarchy) { _blockWatch = null; return; }
            foreach (var fsm in _blockWatch.GetComponents<PlayMakerFSM>())
            {
                // Hazard denials land in DEBRIS, not BLOCKED (sync pass
                // MED-6); both exit within a frame, so the entry
                // subscriptions in Init are the primary catch — this poll
                // is the fallback.
                string state = fsm.ActiveStateName;
                if (state != "BLOCKED" && state != "DEBRIS") continue;
                if (!HasStates(fsm, "Check Fuel", "Travel")) continue; // pipeline class
                _blockWatch = null;
                Table.Say(state == "DEBRIS" ? DebrisSpeech() : BlockedSpeech(fsm));
                return;
            }
        }

        /// <summary>Hazard-denial composition: "Blocked." plus the hazard
        /// class the vector line settled on — the line's state is the only
        /// place the game distinguishes WHY (D21; no text renders).</summary>
        private static string DebrisSpeech()
        {
            string cls = null;
            if (_vectorFsm != null)
                switch (_vectorFsm.ActiveStateName)
                {
                    case "Blocked Debris": cls = Lex.T("map.block.debris"); break;
                    case "Blocked Rad": cls = Lex.T("map.block.radiation"); break;
                    case "Blocked Blockade": cls = Lex.T("map.block.blockade"); break;
                    case "Blocked Bounty": cls = Lex.T("map.block.bounty"); break;
                }
            return cls != null
                ? Lex.T("map.blocked") + " " + cls : Lex.T("map.blocked");
        }

        private static void OnDenialState(PlayMakerFSM fsm, bool debris)
        {
            if (_blockWatch == null || fsm == null
                || fsm.gameObject != _blockWatch) return;
            if (!HasStates(fsm, "Check Fuel", "Travel")) return;
            _blockWatch = null;
            Table.Say(debris ? DebrisSpeech() : BlockedSpeech(fsm));
        }

        /// <summary>Blocked-travel composition (owner ruling 2026-08-01): when
        /// fuel was the reason — decided from the SAME variables the game's own
        /// Check Fuel compared — restate the requirement: "Not enough fuel.
        /// Requires N fuel." Hazard/unknown blocks keep the sanctioned generic
        /// word plus whatever the Vector Display renders (raw-logged).</summary>
        private static string BlockedSpeech(PlayMakerFSM fsm)
        {
            string cost = null;
            var s = fsm.FsmVariables.GetFsmString("Fuel Cost");
            if (s != null && !string.IsNullOrEmpty(s.Value)) cost = s.Value.Trim();
            if (cost == null)
            {
                var ci = fsm.FsmVariables.GetFsmInt("Fuel Cost Int");
                if (ci != null && ci.Value > 0) cost = ci.Value.ToString();
            }
            float? fuel = null;
            var pf = fsm.FsmVariables.GetFsmFloat("Player Fuel");
            if (pf != null) fuel = pf.Value;
            else
            {
                var pi = fsm.FsmVariables.GetFsmInt("Player Fuel");
                if (pi != null) fuel = pi.Value;
            }

            if (cost != null && fuel.HasValue
                && float.TryParse(cost, out float costValue) && fuel.Value < costValue)
                return Lex.T("map.blocked.fuel") + " " + Lex.T("map.requires")
                    + " " + cost + " " + Lex.T("map.fuel-unit");

            var sb = new System.Text.StringBuilder(Lex.T("map.blocked"));
            var display = GameQueries.MapRoot() != null
                ? GameQueries.MapRoot().Find("Vector Display") : null;
            if (display != null && display.gameObject.activeInHierarchy)
            {
                foreach (var tmp in display.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (!Util.RenderedUp(tmp.transform)) continue;
                    string text = SpeechService.Clean(tmp.text);
                    if (string.IsNullOrEmpty(text)) continue;
                    sb.Append(' ').Append(text).Append('.');
                    Plugin.Log.LogInfo("[Map] BLOCKED render capture: \"" + text + "\"");
                }
            }
            else
            {
                Plugin.Log.LogInfo("[Map] BLOCKED with no rendered Vector Display — capture");
            }
            return sb.ToString();
        }

        // ---------- Reachability mirror (owner design 2026-08-03; D21) ----------

        private static readonly Dictionary<StationAtlas.Node, string> _noRoute
            = new Dictionary<StationAtlas.Node, string>();
        private static readonly Dictionary<StationAtlas.Node, string> _hazardMirror
            = new Dictionary<StationAtlas.Node, string>();
        private static readonly Dictionary<StationAtlas.Node, int> _fuelSort
            = new Dictionary<StationAtlas.Node, int>();
        private static readonly RaycastHit2D[] _hits = new RaycastHit2D[32];
        // Audited verdicts (owner ruling 2026-08-03: the game's own drawn line
        // WINS over the mirror wherever it has settled — render first, backing
        // second; the Holm's Rock false "Debris field" class). Keyed by node
        // NAME (nodes rebuild per query); "" = audited clear. Cleared when the
        // route origin changes — every verdict is origin-relative.
        private static readonly Dictionary<string, string> _lineOverride
            = new Dictionary<string, string>();
        private static string _overrideOrigin;

        /// <summary>The Can't-travel cell: why this node is out of reach.
        /// Fuel speaks the standing composition (pipeline order — the game
        /// checks fuel before the line); hazards speak their class.</summary>
        private static string NoRouteCell(StationAtlas.Node node)
            => _noRoute.TryGetValue(node, out string r) ? r : null;

        private static string NoRouteReason(Vector3 from, StationAtlas.Node node, out int cost)
        {
            cost = int.MaxValue;
            string costStr = FuelCostOf(node);
            if (costStr != null && int.TryParse(costStr, out int c)) cost = c;
            else LogOnce("[Map] belt node \"" + node.Name
                + "\" publishes no fuel cost — sorted last");
            // Hazard verdict computed unconditionally: the live-line audit
            // compares it even when fuel is the spoken reason.
            string hazard = HazardBlock(from, node.Root.position);
            if (node.Name != null
                && _lineOverride.TryGetValue(node.Name, out string audited))
                hazard = audited.Length == 0 ? null : audited;
            _hazardMirror[node] = hazard ?? "";
            // Unrounded fuel (sync pass MED-7: travel deducts a float — the
            // game's own Check Fuel is a FloatCompare, so is the mirror).
            float? fuel = LuaStore.NumF("Player_Fuel");
            string fuelReason = fuel.HasValue && cost != int.MaxValue && cost > fuel.Value
                ? Lex.T("map.blocked.fuel") + " " + Lex.T("map.requires")
                    + " " + cost + " " + Lex.T("map.fuel-unit")
                : null;
            // BOTH reasons when both hold (owner bug 2026-08-03: every
            // blocked node was also fuel-short, so fuel-first precedence
            // hid every hazard — fuel changes per cycle, hazards are the
            // structural fact). Fuel keeps the game's check order, the
            // hazard follows.
            if (fuelReason != null && hazard != null) return fuelReason + " " + hazard;
            return fuelReason ?? hazard;
        }

        /// <summary>The hazard class blocking the straight route, mirrored
        /// over the game's own colliders (D21: the vector line's tag classes,
        /// haznav bypasses debris, transponder bypasses bounty). First hit
        /// along the route wins, like the line's first contact. Null =
        /// clear.</summary>
        private static string HazardBlock(Vector3 from, Vector3 to)
        {
            bool haznav = (LuaStore.NumF("Ship_Haznav") ?? 0f) >= 1f;
            bool transponder = (LuaStore.NumF("Ship_Transponder") ?? 0f) >= 1f;
            // Exact endpoint — NO overshoot. The 1.1 factor inferred from
            // the Aim leg proved wrong in world units (live audit
            // 2026-08-03: the overshot mirror blocked a node the real line
            // cleared — the game's sweep length is screen-scaled). The
            // audit watches for the opposite error.
            var filter = default(ContactFilter2D);
            filter.NoFilter();
            filter.useTriggers = true;
            int count = Physics2D.Linecast(from, to, filter, _hits);
            for (int i = 0; i < count && i < _hits.Length; i++)
            {
                var col = _hits[i].collider;
                if (col == null) continue;
                switch (col.tag)
                {
                    case "Map Debris":
                        if (!haznav) return Lex.T("map.block.debris");
                        break;
                    case "Map Radiation": return Lex.T("map.block.radiation");
                    case "Map Blockade": return Lex.T("map.block.blockade");
                    case "Map Bounty":
                        if (!transponder) return Lex.T("map.block.bounty");
                        break;
                }
            }
            return null;
        }

        private static Vector3 ShipAnchor()
        {
            var g = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmGameObject("Sleeper Vector");
            return g != null && g.Value != null
                ? g.Value.transform.position : Vector3.zero;
        }

        // The live vector line audits the mirror: whenever it settles a
        // verdict for the focused node, disagreement logs loudly AND the line
        // wins (owner ruling 2026-08-03) — the verdict overrides the mirror
        // for that node and the rows rebuild so sections and reason cells
        // recompose. Staleness self-heals: an override that later disagrees
        // with the line (upgrade bought mid-view) re-audits the same way.
        private static PlayMakerFSM _vectorFsm;
        private static StationAtlas.Node _auditNode;
        private static bool _auditDone;
        private static bool _auditArmed;

        private static void VectorAuditTick()
        {
            if (_rowsPlane == null || _rowsPlane.name != "Zoomed Out Map UI") return;
            int row = Table.Row;
            if (row < 0 || row >= _rows.Count) return;
            var node = _rows[row].Node;
            if (node == null || node.IsHere) { _auditNode = null; return; }
            if (_auditNode != node)
            {
                _auditNode = node;
                _auditDone = false;
                // Re-aim takes ≥1 frame after selection moves (sync pass
                // MED-4: the audit read the PREVIOUS node's resting verdict
                // and attributed it here). Arm only after the line visibly
                // left its settled state for this node.
                _auditArmed = false;
            }
            if (_auditDone) return;
            if (_vectorFsm == null)
            {
                var go = GameObject.Find("Letterbox Canvas/Map Screen/Sleeper Location");
                _vectorFsm = go != null ? go.GetComponent<PlayMakerFSM>() : null;
                if (_vectorFsm == null) return;
            }
            string lineClass;
            switch (_vectorFsm.ActiveStateName)
            {
                // Finish Vector Haz Nav / Finish Vector Transponder are NOT
                // terminal (sync pass MED-4: both keep watching for further
                // hazards) — they arm like any transit state.
                case "Vector Good": lineClass = ""; break;
                case "Blocked Debris": lineClass = Lex.T("map.block.debris"); break;
                case "Blocked Rad": lineClass = Lex.T("map.block.radiation"); break;
                case "Blocked Blockade": lineClass = Lex.T("map.block.blockade"); break;
                case "Blocked Bounty": lineClass = Lex.T("map.block.bounty"); break;
                default: _auditArmed = true; return; // aiming/transit for THIS node
            }
            if (!_auditArmed) return; // stale verdict from the previous aim
            _auditDone = true;
            string mirror = _hazardMirror.TryGetValue(node, out string h) ? h : "";
            // Verdict logs on AGREEMENT too (ride finding 2026-08-03: a
            // silent audit can't be told apart from one that never settled —
            // the Holm's Rock re-test was inconclusive for that reason).
            if (mirror == lineClass)
                Plugin.Log.LogInfo("[Map] line audit settled: " + node.Name
                    + " = " + (lineClass == "" ? "clear" : lineClass)
                    + " — mirror agrees");
            if (mirror != lineClass)
            {
                Plugin.Log.LogWarning("[Map] REACH MIRROR MISMATCH on \""
                    + node.Name + "\": line says \""
                    + (lineClass == "" ? "clear" : lineClass)
                    + "\", mirror said \""
                    + (mirror == "" ? "clear" : mirror)
                    + "\" — line wins, row corrected");
                if (node.Name != null)
                {
                    _lineOverride[node.Name] = lineClass;
                    _builtAt = -1f; // sections and reason cells recompose
                }
            }
        }

        // ---------- Rows ----------

        private static List<RowModel> Rows()
        {
            // Plane-keyed cache (sync pass F5): a native zoom flip must never
            // serve the old plane's rows, however fresh they are.
            var plane = StationAtlas.ActiveMapPlane();
            // Ambiguous/transition beat (null plane — the two-raised-planes
            // fix): keep serving the last good plane's rows instead of an
            // empty table (sync pass LOW-11); the next honest plane rebuilds.
            if (plane == null && _rowsPlane != null
                && _rowsPlane.gameObject.activeInHierarchy) plane = _rowsPlane;
            if (plane == _rowsPlane
                && Time.unscaledTime - _builtAt <= CacheWindow) return _rows;
            _builtAt = Time.unscaledTime;
            _rowsPlane = plane;
            _rows = new List<RowModel>();
            _sections = new List<string>();

            if (plane != null)
            {
                var nodes = StationAtlas.BuildMapNodes(plane);
                if (plane.name == "Zoomed Out Map UI")
                {
                    // Reachability-first belt table (owner design 2026-08-03):
                    // current location on top, in-reach nodes cheapest-fuel-
                    // first, out-of-reach after them with a Can't-travel
                    // reason cell. The game stores NO per-node reachability
                    // (D21: dials placeholdered, the vector line is per-
                    // focus) — the mirror reads the SAME inputs the line
                    // does: the button's published fuel cost vs the store's
                    // player fuel, and a linecast over the game's own hazard
                    // colliders. The live line audits the mirror at every
                    // focus settle (VectorAuditTick).
                    _noRoute.Clear();
                    _hazardMirror.Clear();
                    _fuelSort.Clear();
                    StationAtlas.Node here = null;
                    foreach (var n in nodes) if (n.IsHere) { here = n; break; }
                    // Origin = the game's own line origin (sync pass MED-5:
                    // 30358 aims FROM the Sleeper Vector object itself); the
                    // occupied marker is the fallback approximation.
                    Vector3 from = ShipAnchor();
                    if (from == Vector3.zero && here != null) from = here.Root.position;
                    // Audited overrides are origin-relative: a new origin
                    // (travel landed) invalidates every settled verdict.
                    string originKey = here != null ? here.Name : from.ToString();
                    if (originKey != _overrideOrigin)
                    {
                        _lineOverride.Clear();
                        _overrideOrigin = originKey;
                    }
                    var reach = new List<StationAtlas.Node>();
                    var blocked = new List<StationAtlas.Node>();
                    bool anyCost = false;
                    foreach (var n in nodes)
                    {
                        if (n.IsHere) continue;
                        string reason = NoRouteReason(from, n, out int cost);
                        if (cost != int.MaxValue) anyCost = true;
                        _fuelSort[n] = cost;
                        if (reason == null) reach.Add(n);
                        else { _noRoute[n] = reason; blocked.Add(n); }
                    }
                    // Boot beat: the buttons' Cost Setup hasn't published yet
                    // (log 2026-08-03: every node costless on the first
                    // build) — don't cache a blind sort; rebuild next call.
                    if (!anyCost && (reach.Count > 0 || blocked.Count > 0))
                        _builtAt = -1f;
                    reach.Sort((a, b) => _fuelSort[a].CompareTo(_fuelSort[b]));
                    blocked.Sort((a, b) => _fuelSort[a].CompareTo(_fuelSort[b]));
                    if (here != null)
                    {
                        _sections.Add(Lex.T("map.sec.here"));
                        _rows.Add(new RowModel { Node = here, Section = _sections.Count - 1 });
                    }
                    if (reach.Count > 0)
                    {
                        int section = _sections.Count;
                        _sections.Add(Lex.T("map.sec.inreach"));
                        foreach (var n in reach)
                            _rows.Add(new RowModel { Node = n, Section = section });
                    }
                    if (blocked.Count > 0)
                    {
                        int section = _sections.Count;
                        _sections.Add(Lex.T("map.sec.outreach"));
                        foreach (var n in blocked)
                            _rows.Add(new RowModel { Node = n, Section = section });
                    }
                }
                else
                {
                    // Local plane: one sector's contract markers, single section.
                    nodes.Sort(ByMapPosition);
                    _sections.Add(SectorOfPlane(plane) + Lex.T("map.plane.contracts"));
                    foreach (var n in nodes)
                        _rows.Add(new RowModel { Node = n, Section = 0 });
                    // Empty sector: the Sector Display's own rendered plate
                    // ("NO ACTIVE CONTRACTS") is the section's one row (owner-
                    // approved shape 2026-08-01). Read live at speech time.
                    if (nodes.Count == 0 && NoContractsSpeech() != null)
                        _rows.Add(new RowModel
                        {
                            Section = 0,
                            OpSpeech = () => NoContractsSpeech() ?? Lex.T("map.empty"),
                        });
                }
            }

            // The Belt Button ops row (owner ruling 2026-08-01): exists whenever
            // the button renders — its rendered label IS the gating story.
            var belt = BeltButtonGo();
            if (belt != null)
            {
                int section = _sections.Count;
                _sections.Add(Lex.T("map.section.controls"));
                var ui = belt.GetComponent<UnityEngine.UI.Button>();
                if (ui == null) ui = belt.GetComponentInChildren<UnityEngine.UI.Button>(false);
                _rows.Add(new RowModel
                {
                    Section = section,
                    OpTarget = ui != null ? ui.gameObject : belt,
                    OpSpeech = () => BeltRowSpeech(belt),
                });
            }
            return _rows;
        }

        /// <summary>The Sector Display's rendered no-contracts plate text.
        /// Null = the plate isn't drawn (render is the existence test).</summary>
        private static string NoContractsSpeech()
        {
            var root = GameQueries.MapRoot();
            var t = root != null ? root.Find("Sector Display/No Contracts") : null;
            if (t == null || !t.gameObject.activeInHierarchy || !Util.RenderedUp(t)) return null;
            foreach (var tmp in t.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static GameObject BeltButtonGo()
        {
            var root = GameQueries.MapRoot();
            var t = root != null ? root.Find("Belt Button") : null;
            if (t == null || !t.gameObject.activeInHierarchy || !Util.RenderedUp(t)) return null;
            return t.gameObject;
        }

        private static string BeltRowSpeech(GameObject go)
        {
            string label = Describe.FirstText(go) ?? go.name;
            var sb = new System.Text.StringBuilder(label);
            sb.Append(Lex.T("topbar.button-suffix"));
            var ui = go.GetComponent<UnityEngine.UI.Button>();
            if (ui == null) ui = go.GetComponentInChildren<UnityEngine.UI.Button>(false);
            if (ui != null && !ui.IsInteractable())
                sb.Append(' ').Append(Lex.T("zone.disabled"));
            return sb.ToString();
        }

        /// <summary>Sector identity of a belt marker, from the marker root's
        /// "&lt;sector&gt; Location [variant]" shape (D8 census; sector names are
        /// game-sanctioned map labels). A marker outside the shape logs loudly
        /// and groups under the unknown section — never silently dropped.</summary>
        private static string SectorOf(StationAtlas.Node node)
        {
            string name = node.Root != null ? node.Root.name : "";
            int i = name.IndexOf(" Location", StringComparison.Ordinal);
            if (i > 0) return name.Substring(0, i);
            LogOnce("[Map] marker outside the '<sector> Location' shape: " + name);
            return Lex.T("map.section.unknown");
        }

        private static string SectorOfPlane(Transform plane)
        {
            const string suffix = " Contracts";
            string name = plane.name;
            return name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length) : name;
        }

        private static string PlaneLabel(Transform plane)
            => plane.name == "Zoomed Out Map UI"
                ? Lex.T("map.plane.belt")
                : SectorOfPlane(plane) + Lex.T("map.plane.contracts");

        private static int ByMapPosition(StationAtlas.Node a, StationAtlas.Node b)
        {
            var pa = a.Root.position;
            var pb = b.Root.position;
            int cmp = pa.x.CompareTo(pb.x);
            return cmp != 0 ? cmp : pb.y.CompareTo(pa.y);
        }

        private static int RowOfSelected()
        {
            int strict = RowOfSelectedStrict();
            return strict >= 0 ? strict : 0; // entry anchor parks at row 0
        }

        /// <summary>The row owning the game's current selection, or -1 when
        /// selection maps to no marker (excursion UI, anchors, sub-windows).
        /// A stacked sibling's button (the collapse keeps only the topmost
        /// copy) matches by marker position — same drawn spot, same row.</summary>
        private static int RowOfSelectedStrict()
        {
            var selected = Navigator.Current();
            if (selected == null) return -1;
            var rows = Rows();
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Node != null && rows[i].Node.Button == selected) return i;
            Transform root = null;
            for (var p = selected.transform; p != null; p = p.parent)
                if (p.name.Contains(" Location")) { root = p; break; }
            if (root == null) return -1;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Node != null
                    && (rows[i].Node.Root.position - root.position).sqrMagnitude <= 1f)
                    return i;
            return -1;
        }

        /// <summary>True when the current row owns a selectable marker button
        /// — the only rows whose game-selection the watchdog may trust as
        /// "moved away" (ops and you-are-here rows never carry selection).</summary>
        private static bool CurrentRowAnchored()
        {
            var rows = Rows();
            int row = Table.Row;
            if (row < 0 || row >= rows.Count) return true;
            var n = rows[row].Node;
            return n != null && n.Button != null && !n.IsHere;
        }

        // ---------- Speech composition (reads live at speech time) ----------

        private static string RowArriveSpeech(int row, int col)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("map.empty");
            var r = rows[row];
            if (r.Node == null) return r.OpSpeech();
            if (col <= 0) return CompressedReport(r.Node);
            return NameCell(r.Node) + " " + CellRead(row, col);
        }

        private static string CellRead(int row, int col)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("map.empty");
            var r = rows[row];
            if (r.Node == null) return r.OpSpeech();
            var cols = ColumnsFor(r.Node);
            if (col < 0) col = 0;
            if (col >= cols.Count) col = cols.Count - 1; // row without the parked facet
            var column = cols[col];
            string content = column.Cell(r.Node);
            if (string.IsNullOrEmpty(content)) content = Lex.T(column.EmptyKey);
            string header = column.Header != null ? column.Header(r.Node) : null;
            if (header == null) header = Lex.T(column.HeaderKey);
            return header + ": " + content;
        }

        private static string FullReport(int row)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("map.empty");
            if (rows[row].Node == null) return rows[row].OpSpeech();
            var sb = new System.Text.StringBuilder();
            int count = ColumnsFor(rows[row].Node).Count;
            for (int c = 0; c < count; c++)
                sb.Append(CellRead(row, c)).Append(' ');
            return sb.ToString().TrimEnd();
        }

        /// <summary>Compressed row report (the zone idiom): name + flags, then
        /// only the facets with content, unheadered description last.</summary>
        private static string CompressedReport(StationAtlas.Node node)
        {
            var sb = new System.Text.StringBuilder(NameCell(node));
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
            string fuel = FuelCostOf(node);
            if (fuel != null)
                sb.Append(' ').Append(Lex.T("map.col.fuel")).Append(": ")
                  .Append(fuel).Append('.');
            var card = ContractCard(node);
            if (card != null)
            {
                // Card facets in the ruled order: risk bare (the location-table
                // idiom), body unheadered, requirement/payment behind the card's
                // own rendered labels, client last. NameCell already carried
                // the subhead.
                string risk = CardText(card, "Type");
                if (risk != null) sb.Append(' ').Append(risk).Append('.');
                string body = CardText(card, "Description");
                if (body != null) sb.Append(' ').Append(body);
                AppendCardBlock(sb, card, "Delivery", "Description/Delivery/Delivery",
                    "map.col.requirement");
                AppendCardBlock(sb, card, "Payment", "Description/Payment/Payment",
                    "map.col.payment");
                string client = CardText(card, "Client");
                if (client != null)
                    sb.Append(' ').Append(Lex.T("map.col.client")).Append(": ")
                      .Append(client).Append('.');
            }
            else
            {
                string desc = StationAtlas.ReadDescription(node);
                if (!string.IsNullOrEmpty(desc)) sb.Append(' ').Append(desc).Append('.');
            }
            return sb.ToString();
        }

        private static void AppendCardBlock(System.Text.StringBuilder sb,
            Transform card, string block, string valuePath, string fallbackKey)
        {
            string value = CardText(card, valuePath);
            if (value == null) return;
            string label = SlashLabel(CardText(card, "Description/" + block + "/Target"));
            sb.Append(' ').Append(label ?? Lex.T(fallbackKey)).Append(": ")
              .Append(value).Append('.');
        }

        private static string NameCell(StationAtlas.Node node)
        {
            var facet = StationAtlas.ReadNameFacet(node);
            var sb = new System.Text.StringBuilder(facet.Name).Append('.');
            if (node.IsHere) sb.Append(' ').Append(Lex.T("map.here"));
            if (facet.IsNew) sb.Append(' ').Append(Lex.T("zone.new"));
            if (!node.IsHere && facet.Disabled) sb.Append(' ').Append(Lex.T("zone.disabled"));
            // Owner ruling 2026-08-01: on card rows the marker subhead is part
            // of the Name cell — the Description column carries the card body.
            if (ContractCard(node) != null)
            {
                string subhead = StationAtlas.ReadDescription(node);
                if (!string.IsNullOrEmpty(subhead))
                    sb.Append(' ').Append(subhead).Append('.');
            }
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

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void LogOnce(string line)
        {
            if (Logged.Add(line)) Plugin.Log.LogWarning(line);
        }
    }
}
