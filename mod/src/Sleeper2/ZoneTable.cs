using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The station zone table — the general node-table binding on the
    /// TableEngine (owner design 2026-07-26): rows are StationAtlas nodes, row
    /// arrival drives the camera to the node (OnRowArrive, the engine's documented
    /// camera-sync hook), row speech is the rendered billboard, Enter clicks the
    /// node's Location Button through the game's machinery. The same binding shape
    /// serves any future node provider (contract, rig, map) — variation lives in
    /// the provider, not here.
    ///
    /// Coexists with the game's native WASD camera walk: manual panning speaks
    /// through the focus path (selector -> EventSystem -> Describe's location
    /// read), table walking speaks table-side and suppresses the selector's echo
    /// for a beat (the CS1 map-table suppression, ported via NoteDrive).</summary>
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

        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => Nodes().Count,
            Cols = _ => 1,
            RowSpeech = (r, c) => RowRead(r),
            CellSpeech = (r, c) => RowRead(r),
            EmptyRow = () => Lex.T("zone.empty"),
            EmptyCol = () => Lex.T("zone.empty"),
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
                _driveEchoUntil = Time.unscaledTime + 1.5f;
                HoverNode(_hovered, true);
                HoverNode(button, false);
                _hovered = button;
            };
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

        /// <summary>The table owns arrows/Enter in open-station mode: gameplay HUD
        /// up (atlas has nodes), no conversation, no tutorial, no action view.
        /// Gates re-verify per ride; each is the game's own dial.</summary>
        public static bool Active()
        {
            if (ConversationEvents.ConversationActive) return false;
            if (TutorialReader.Active()) return false;
            var actionView = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmBool("Action View?");
            if (actionView != null && actionView.Value) return false;
            // Rig overlay: its own surface owns the keys and the station camera
            // is parked (Focus FSM Disabled, live capture 2026-07-26).
            var rigView = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmBool("RIG view");
            if (rigView != null && rigView.Value) return false;
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

        /// <summary>Live billboard read — runs AFTER OnRowArrive in the engine's
        /// MoveRow flow, so the hover's synchronous FSM reaction has already
        /// rendered the description this reads (owner ruling: capture from the
        /// highlight, never from a snapshot).</summary>
        private static string RowRead(int row)
        {
            var nodes = Nodes();
            if (row < 0 || row >= nodes.Count) return Lex.T("zone.empty");
            return StationAtlas.Read(nodes[row], Lex.T("zone.new"));
        }
    }
}
