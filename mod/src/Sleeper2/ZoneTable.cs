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
            Rows = () => Nodes().Count,
            Cols = _ => Columns.Length,
            RowSpeech = (r, c) => RowArriveSpeech(r, c),
            CellSpeech = (r, c) => CellRead(r, c),
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
                if (row < 0 || row >= nodes.Count) return;
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
                if (row < 0 || row >= nodes.Count) return;
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
            return Nodes().Count > 0;
        }

        public static bool HandleKeys()
        {
            if (!_entered)
            {
                // First entry: start the walk where the camera already is — the
                // selector's current pick, the game's own truth.
                _entered = true;
                Table.Reset(RowOfClosest());
            }
            return Table.HandleKeys();
        }

        public static void Tick()
        {
            if (!Active())
            {
                _entered = false;
                if (_hovered != null)
                {
                    HoverNode(_hovered, true);
                    _hovered = null;
                }
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
            if (row < 0 || row >= nodes.Count) return Lex.T("zone.empty");
            if (col <= 0) return CompressedReport(nodes[row]);
            var facet = StationAtlas.ReadNameFacet(nodes[row]);
            return NameCell(facet) + " " + CellRead(row, col);
        }

        /// <summary>Facet browse: header-labeled cell, stable geometry — an empty
        /// facet speaks its terse emptiness, never skips (CS1 ruling 3).</summary>
        private static string CellRead(int row, int col)
        {
            var nodes = Nodes();
            if (row < 0 || row >= nodes.Count) return Lex.T("zone.empty");
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
            if (row < 0 || row >= nodes.Count) return Lex.T("zone.empty");
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
