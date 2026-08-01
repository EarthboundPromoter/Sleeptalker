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

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Nodes().Count + Ops().Count,
            Cols = row => row < Nodes().Count ? Columns.Length : 1,
            RowSpeech = (r, c) => RowArriveSpeech(r, c),
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

        public static void Init()
        {
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
                // Facet reads run after this hover, so the billboard is rendered.
                _driveEchoUntil = Time.unscaledTime + 1.5f;
                HoverNode(_hovered, true);
                HoverNode(button, false);
                _hovered = button;
            };
            Table.Detail = (row, col) => Table.Say(FullReport(row));
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

        private static void ExitTable()
        {
            _entered = false;
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
                && Util.AlphaUpTo(endCycle) >= 0.05f)
            {
                var button = FindDeep(endCycle, "Dice Slot Button");
                _ops.Add(new OpRow
                {
                    Target = button != null && button.gameObject.activeInHierarchy
                        ? button.gameObject : null,
                    Speech = () => EndCycleSpeech(endCycle, button),
                });
            }

            var crew = display.transform.Find("Crew Assignment/Display");
            if (crew != null && crew.gameObject.activeInHierarchy)
            {
                foreach (var b in crew.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                {
                    var go = b.gameObject;
                    if (Util.AlphaUpTo(go.transform) < 0.05f) continue;
                    _ops.Add(new OpRow
                    {
                        Target = go,
                        Speech = () => (Describe.FirstText(go) ?? go.name)
                            + Lex.T("topbar.button-suffix"),
                    });
                }
            }
            return _ops;
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
                if (Util.AlphaUpTo(tmp.transform, endCycle) < 0.05f) continue;
                string text = SpeechService.Clean(tmp.text);
                if (string.IsNullOrEmpty(text)) continue;
                sb.Append(". ").Append(text);
                break; // the active cost-strip line
            }
            sb.Append('.');
            return sb.ToString();
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
            if (col <= 0) return CompressedReport(nodes[row]);
            var facet = StationAtlas.ReadNameFacet(nodes[row]);
            return NameCell(facet) + " " + CellRead(row, col);
        }

        /// <summary>Facet browse: header-labeled cell, stable geometry — an empty
        /// facet speaks its terse emptiness, never skips (CS1 ruling 3).</summary>
        private static string CellRead(int row, int col)
        {
            var nodes = Nodes();
            if (row >= nodes.Count) return OpSpeech(row);
            if (row < 0) return Lex.T("zone.empty");
            if (col < 0 || col >= Columns.Length) col = 0;
            var column = Columns[col];
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
            for (int c = 0; c < Columns.Length; c++)
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
