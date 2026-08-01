using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

using Sleeptalker.Scaffold;

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
    /// Columns (owner-ruled): Name (New Shine + you-are-here ride it) | Clock
    /// (EXISTENCE-BASED per the 2026-07-31 amendment) | Drives (D1 pips) |
    /// Description. NO Actions column (markers carry no action groups), NO Fuel
    /// column v1 (nothing renders fuel at marker level — the Travel Confirm
    /// window's own rendered cost reads natively at commit).
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
    /// renders (suspension via the Active gate — focus reads carry them) and
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
        }

        private static readonly Column[] Columns =
        {
            new Column { HeaderKey = "zone.col.name",
                Cell = n => NameCell(n), EmptyKey = "map.empty" },
            new Column { HeaderKey = "zone.col.clock",
                Cell = ClockCell, EmptyKey = "zone.clock.none" },
            new Column { HeaderKey = "zone.col.drives",
                Cell = DrivesCell, EmptyKey = "zone.drives.none" },
            new Column { HeaderKey = "zone.col.description",
                Cell = StationAtlas.ReadDescription, EmptyKey = "zone.desc.none" },
        };

        /// <summary>Existence-based Clock cell (owner amendment 2026-07-31, carried
        /// from the zone table): the cell exists only when a rendered clock does.</summary>
        private static List<Column> ColumnsFor(StationAtlas.Node node)
        {
            var cols = new List<Column>(Columns.Length);
            var scratch = new List<string>();
            foreach (var column in Columns)
            {
                if (column.HeaderKey == "zone.col.clock"
                    && !StationAtlas.ReadClock(node, scratch)) continue;
                cols.Add(column);
            }
            return cols;
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
            Table.OnRowArrive = (prev, row) =>
            {
                var rows = Rows();
                if (row < 0 || row >= rows.Count) return;
                var node = rows[row].Node;
                if (node == null || node.Button == null || node.IsHere) return;
                if (!node.Button.activeInHierarchy) return;
                var es = EventSystem.current;
                if (es == null || es.currentSelectedGameObject == node.Button) return;
                // The map's own follow: plain selection — the scroll view chases
                // it and the marker mirrors it (Selected behaves like hover, so
                // the billboard renders before the facet reads). CAUTION of
                // record: off-screen markers drop CanvasGroup.interactable and
                // ping the global Reselector — ride capture whether selection
                // lands after the scroll chase.
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
        private static bool TravelCommitInFlight()
        {
            var rows = Rows();
            for (int i = 0; i < rows.Count; i++)
            {
                var node = rows[i].Node;
                if (node == null || node.Button == null) continue;
                foreach (var fsm in node.Button.GetComponents<PlayMakerFSM>())
                {
                    string state = fsm.ActiveStateName;
                    if (state != "Travel Confirm?" && state != "Travel"
                        && state != "End Cycle" && state != "Fade Up") continue;
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
                if (_entered || _lastPlane != null) Exit();
                return;
            }
            var plane = StationAtlas.ActiveMapPlane();
            if (plane == null) return;                    // the Trans beat between planes
            if (_lastPlane == null) { _lastPlane = plane; return; } // opening plane (always local) — silent
            if (plane == _lastPlane) return;
            _lastPlane = plane;
            _builtAt = -1f;
            _entered = false;
            Table.Reset();
            Table.Say(PlaneLabel(plane));
        }

        public static void OnSceneChanged() => Exit();

        private static void Exit()
        {
            _entered = false;
            _lastPlane = null;
            _builtAt = -1f;
            _selectEchoUntil = -1f; // the close-time RefocusUI re-pick must speak
            Table.Reset();
        }

        // ---------- Rows ----------

        private static List<RowModel> Rows()
        {
            // Plane-keyed cache (sync pass F5): a native zoom flip must never
            // serve the old plane's rows, however fresh they are.
            var plane = StationAtlas.ActiveMapPlane();
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
                    // Belt: sectors as sections, ordered by the map's own
                    // geometry (group centroid left-to-right, markers likewise).
                    var groups = new List<Group>();
                    foreach (var n in nodes)
                    {
                        string sector = SectorOf(n);
                        Group g = null;
                        foreach (var x in groups)
                            if (x.Sector == sector) { g = x; break; }
                        if (g == null) { g = new Group { Sector = sector }; groups.Add(g); }
                        g.Nodes.Add(n);
                    }
                    foreach (var g in groups)
                    {
                        g.Nodes.Sort(ByMapPosition);
                        float sum = 0f;
                        foreach (var n in g.Nodes) sum += n.Root.position.x;
                        g.X = sum / g.Nodes.Count;
                    }
                    groups.Sort((a, b) => a.X.CompareTo(b.X));
                    foreach (var g in groups)
                    {
                        int section = _sections.Count;
                        _sections.Add(g.Sector + Lex.T("map.section.suffix"));
                        foreach (var n in g.Nodes)
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
            var selected = Navigator.Current();
            if (selected == null) return 0;
            var rows = Rows();
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Node != null && rows[i].Node.Button == selected) return i;
            return 0; // the anchor (or a sub-window remnant) parks at row 0
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
            return Lex.T(column.HeaderKey) + ": " + content;
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
            string desc = StationAtlas.ReadDescription(node);
            if (!string.IsNullOrEmpty(desc)) sb.Append(' ').Append(desc).Append('.');
            return sb.ToString();
        }

        private static string NameCell(StationAtlas.Node node)
        {
            var facet = StationAtlas.ReadNameFacet(node);
            var sb = new System.Text.StringBuilder(facet.Name).Append('.');
            if (node.IsHere) sb.Append(' ').Append(Lex.T("map.here"));
            if (facet.IsNew) sb.Append(' ').Append(Lex.T("zone.new"));
            if (!node.IsHere && facet.Disabled) sb.Append(' ').Append(Lex.T("zone.disabled"));
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
