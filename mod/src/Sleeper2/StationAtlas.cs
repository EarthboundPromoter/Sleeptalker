using System.Collections.Generic;
using TMPro;
using UnityEngine;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Station atlas — the table-agnostic node provider (owner design
    /// 2026-07-26: general core, per-family variation as data; CS1 StationAtlas is
    /// the baseline, improved not copied).
    ///
    /// Discovery is mechanism-keyed, never name-listed: a node is a child of an
    /// active "Locations" container whose subtree carries the census-universal
    /// vocabulary (Location Contents / Billboard Elements / Marker / Location
    /// Button; Portrait Name + Description render the identity). Containers are
    /// scanned once per scene and cached; nodes rebuild fresh per query (live-truth
    /// rule). The availability dial is the node root FSM's ActiveStateName — CS1
    /// owner ruling 4 carries: NOT SHOWN = NOT KNOWN, so only whitelisted dial
    /// states list, and every unknown state logs loudly for vocabulary growth.
    ///
    /// Camera: node focus follows ENGINE CONVENTION (owner ruling 2026-07-26) —
    /// the zone table selects a node's Location Button through EventSystem, the
    /// dpad idiom, and the game's own selection machinery drives its authored
    /// per-node camera (Location Cam / Cam Focus). The free-scroll gimbal rig
    /// (WASD / LS) is a parallel path the mod never writes; the UI selector on
    /// the Focus object remains the proximity truth used for entry positioning.</summary>
    internal static class StationAtlas
    {
        internal sealed class Node
        {
            public string Name;          // rendered Portrait Name (discovery-time; spoken reads are live)
            public string State;         // node root FSM ActiveStateName (the dial)
            public Transform Root;       // location node root
            public GameObject Button;    // Marker/Location Button (may be inactive)
            public Transform CamFocus;   // authored per-node camera target (may be null)
            public float Azimuth;        // marker angle around the rig center (deg)
            public bool IsHere;          // map: root FSM in Occupied — the current location (D8)
        }

        // CS1 baseline whitelist; CS2 states join per capture (unknowns log).
        private static readonly HashSet<string> Listed = new HashSet<string>
        {
            "Variables Met", "Off Camera", "Selected", "Cycle Check",
            "Clock Flasher", "Flashed Already?",
        };
        // Ruled ABSENT poles of the dial (owner ruling 2026-08-03): the
        // Off-family states exclude SILENTLY — they are known absence, not
        // vocabulary gaps ("Off"/"Off 2" verified on the same location FSM in
        // the corpus, the duplicate-state wart class; "Off + Destroy" the
        // contract variant). Genuinely unknown states keep logging loudly.
        private static readonly HashSet<string> Absent = new HashSet<string>
        {
            "Off", "Off 2", "Off + Destroy",
        };
        private static readonly HashSet<string> LoggedStates = new HashSet<string>();

        private static readonly List<Transform> Containers = new List<Transform>();
        private static bool _containersFresh;

        public static void InvalidateScene()
        {
            _containersFresh = false;
            _groupContainersFresh = false;
        }

        private static readonly List<Transform> GroupContainers = new List<Transform>();
        private static bool _groupContainersFresh;

        /// <summary>The action-group containers: the canvases park every location's
        /// card group under one of these two. Collected by the SAME sweep that
        /// finds the node containers — that sweep already walks every transform in
        /// memory, and it runs on every scene load, so a second walk for this would
        /// double a cost the audit already flagged as the log's biggest line.</summary>
        private static List<Transform> FindGroupContainers()
        {
            // Force the sweep rather than merely calling it: FindContainers
            // early-returns on ITS own flag, so if the two ever drifted apart
            // this list would stay empty for the session and the location
            // surface would become invisible to the mode model.
            if (!_groupContainersFresh) { _containersFresh = false; FindContainers(); }
            return GroupContainers;
        }

        /// <summary>The location card group the screen is DRAWING, or null when the
        /// player is out on the node surface. This is the render-first answer to
        /// "is the game showing a location?" — see ModeModel.ActionViewUp for why
        /// the game's own "Action View?" bool cannot be trusted to say so.
        ///
        /// Ambiguity returns the FIRST drawn group rather than null, and logs: a
        /// null here would leave the location table entered with no rows, which is
        /// the very soft lock this whole path exists to prevent.</summary>
        private static int _drawnGroupFrame = -1;
        private static Transform _drawnGroup;

        public static Transform DrawnActionGroup()
        {
            // Per frame: the mode model and the location table both ask, and the
            // answer walks an alpha chain per group. One evaluation per frame is
            // the same discipline ModeModel itself uses.
            if (Time.frameCount == _drawnGroupFrame) return _drawnGroup;
            _drawnGroupFrame = Time.frameCount;
            _drawnGroup = DrawnActionGroupUncached();
            return _drawnGroup;
        }

        private static Transform DrawnActionGroupUncached()
        {
            Transform found = null;
            foreach (var container in FindGroupContainers())
            {
                if (container == null) continue;
                foreach (Transform group in container)
                {
                    if (!Util.RenderedUp(group)) continue;
                    if (found == null) { found = group; continue; }
                    LogOnce("[Atlas] two action groups drawn at once: "
                        + found.name + " and " + group.name
                        + " — taking the first (capture)");
                    return found;
                }
            }
            return found;
        }

        /// <summary>Node containers by STRUCTURE, never by name (ride V1, owner
        /// correction — the rig's container is "Rig UI", not "Locations"; the
        /// name key hid a whole subzone): any transform with a direct child
        /// carrying the billboard anatomy (Location Contents/Billboard Elements)
        /// is a container. Covers Hub UI/Locations, Rig UI, Contract UI, and the
        /// Map Screen containers in one rule. Scanned once per scene.</summary>
        private static List<Transform> FindContainers()
        {
            if (_containersFresh) return Containers;
            Containers.Clear();
            GroupContainers.Clear();
            // INCLUDING inactive (ride V3 zone-table kill): the hub Locations
            // container is deactivated on the rig side and during boot beats —
            // a scan that runs in such a moment must still discover it. Activity
            // and alpha are Build()-time filters; discovery is state-blind.
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (!t.gameObject.scene.IsValid()) continue;
                // Action-group containers ride this same sweep (see FindGroupContainers).
                if (t.name == "1_Action Groups" || t.name == "Rig Action Groups")
                    GroupContainers.Add(t);
                bool isContainer = false;
                foreach (Transform child in t)
                {
                    if (child.Find("Location Contents/Billboard Elements") != null)
                    { isContainer = true; break; }
                }
                if (!isContainer) continue;
                if (t.GetComponentInChildren<UnityEngine.UI.Button>(true) == null) continue;
                Containers.Add(t);
                Plugin.Log.LogInfo("[Atlas] container: " + Util.PathOf(t.gameObject));
            }
            _containersFresh = true;
            _groupContainersFresh = true;
            return Containers;
        }

        /// <summary>The discovered node containers, state-blind (crisis
        /// forecast consumer 2026-08-03: unspawned crisis dials rest Off and
        /// never surface as Build() nodes — their criteria are read straight
        /// off the container children).</summary>
        internal static List<Transform> NodeContainers() => FindContainers();

        private static float _lastSelfHeal = -10f;

        // The ride-V1 selection-based zone pick is DELETED (it flapped with hover
        // vs selection and broke the rig exit). Zone identity is dial-driven now:
        // the game deactivates the hub Locations container on the rig side (D9),
        // Build() is alpha-honest (the Map Screen keeps its containers active but
        // alpha-hidden — object presence is never screen presence), and the
        // RigSide dial filters the story states where both sides stay active.

        /// <summary>Fresh node list, azimuth-sorted (walking Down travels one way
        /// around the station). Unlisted dial states are excluded and logged.</summary>
        public static List<Node> Build()
        {
            var nodes = new List<Node>();
            bool rigSide = GameQueries.RigSide();
            foreach (var container in FindContainers())
            {
                if (container == null || !container.gameObject.activeInHierarchy) continue;
                // Render truth: an alpha-hidden container is not on screen (the
                // closed Map Screen keeps its marker containers active — ride V1's
                // phantom rows and zero clocks).
                if (!Util.RenderedUp(container)) continue;
                string path = Util.PathOf(container.gameObject);
                // The map's containers belong to the future map table, never this one.
                if (path.Contains("Map Screen")) continue;
                // Zone by dial (D9): rig side serves rig rooms; station side never does.
                if (rigSide != path.Contains("Rig UI")) continue;
                CollectNodes(container, nodes, 0);
            }
            // Map-axis order (live sweep 2026-07-31): the hub is a VERTICAL arc —
            // the selector's Focus Z walk matches the Y/Z-plane angle DESCENDING
            // (Docks 133.6° … Factory Row 96.8°), and the X/Z azimuth was noise
            // (the owner's out-of-order report).
            nodes.Sort((a, b) => b.Azimuth.CompareTo(a.Azimuth));
            // Self-heal (throttled): an empty zone on a live floor means the
            // container cache is stale (destroyed transforms, additive-load
            // timing) — rediscover rather than stay dead.
            if (nodes.Count == 0 && Time.unscaledTime - _lastSelfHeal > 2f)
            {
                _lastSelfHeal = Time.unscaledTime;
                _containersFresh = false;
            }
            return nodes;
        }

        // ---------- Map planes (D8; the map table's provider seam) ----------

        /// <summary>The ACTIVE map plane (D8 zone-pick rule): belt = Zoomed Out
        /// Map UI, rendered only in belt view; local = the one raised (rendered
        /// AND interactable) "&lt;sector&gt; Contracts" container — the Scroll View
        /// FSM guarantees exactly one; two at once logs loudly. Belt wins when
        /// both read up. Null = map closed or mid-transition (the Trans beat).</summary>
        public static Transform ActiveMapPlane()
        {
            Transform belt = null, sector = null;
            bool ambiguous = false;
            foreach (var container in FindContainers())
            {
                if (container == null || !container.gameObject.activeInHierarchy) continue;
                string path = Util.PathOf(container.gameObject);
                if (!path.Contains("Map Screen")) continue;
                if (!Util.RenderedUp(container)) continue;
                if (container.name == "Zoomed Out Map UI") { belt = container; continue; }
                var group = container.GetComponent<CanvasGroup>();
                if (group != null && !group.interactable) continue;
                if (sector == null) sector = container;
                else
                {
                    // Two raised sector planes = a transition beat where alpha/
                    // interactable are untrustworthy (capture 2026-08-02: 8 of
                    // 10 planes read raised when the map opened over a popup;
                    // first-wins picked Holms Rock by discovery order and the
                    // close announce spoke the wrong sector). Ambiguity IS the
                    // mid-transition state — null per contract; the consumer
                    // keeps its last good plane.
                    LogOnce("[Atlas] two raised sector planes at once: "
                        + sector.name + " and " + container.name + " — ambiguous, null");
                    ambiguous = true;
                }
            }
            if (belt != null) return belt;
            return ambiguous ? null : sector;
        }

        /// <summary>Map-plane nodes (the map table's provider): same billboard
        /// anatomy, same dial whitelist, plus Occupied as the you-are-here row
        /// (D8 — the marker hides its button and parks the ship on itself; it
        /// still renders). No azimuth order — the map table sorts by the map's
        /// own geometry.</summary>
        public static List<Node> BuildMapNodes(Transform plane)
        {
            var nodes = new List<Node>();
            if (plane == null) return nodes;
            CollectNodes(plane, nodes, 0, true);
            // Variant collapse (owner bug 2026-08-03: the story-route copies
            // all draw, stacked exactly on the plain marker — the screen
            // shows ONE node, the table listed three). One drawn position =
            // one row, and the SURVIVOR is the topmost sibling — the native
            // mouse raycast winner. Decode D21: the copies differ ONLY by
            // their button's Transition Scene Name (which travel cutscene
            // plays, once, then self-marks complete); the plain marker's is
            // 'none', so collapsing to the plain copy would SKIP unseen
            // story transitions. Topmost mirrors native play exactly.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (nodes[j].Name != nodes[i].Name) continue;
                    if ((nodes[j].Root.position - nodes[i].Root.position)
                        .sqrMagnitude > 1f) continue;
                    if (nodes[i].Root.GetSiblingIndex()
                        > nodes[j].Root.GetSiblingIndex())
                        nodes[j] = nodes[i];
                    nodes.RemoveAt(i);
                    break;
                }
            }
            return nodes;
        }

        /// <summary>Nodes from a container INCLUDING one level of FSM-less grouping
        /// transforms ("RIG one shots" — the CS1 Post Rim Gate lesson relearned
        /// live 2026-07-31: real nodes nest under groupers).</summary>
        private static void CollectNodes(Transform parent, List<Node> nodes, int depth,
            bool map = false)
        {
            foreach (Transform child in parent)
            {
                var node = NodeOf(child, map);
                if (node != null) { nodes.Add(node); continue; }
                if (depth < 1 && child.gameObject.activeInHierarchy
                    && child.Find("Location Contents") == null && child.childCount > 0)
                    CollectNodes(child, nodes, depth + 1, map);
            }
        }

        private static Node NodeOf(Transform root, bool map = false)
        {
            var fsm = root.GetComponent<PlayMakerFSM>();
            string state = fsm != null ? fsm.ActiveStateName : null;
            var billboard = root.Find("Location Contents/Billboard Elements");
            if (billboard == null) return null; // not a location node
            // Map markers hide by their own CanvasGroup alpha while staying
            // GameObject-active (the root dial's Off/Off 2 set alpha 0 —
            // decode D21); object presence is never screen presence. The
            // drawn-variant capture log is retired: D21 proved TRN copies
            // render nothing distinct (they differ only in travel routing).
            if (map && !Util.RenderedUp(root)) return null;
            // Occupied is map-only vocabulary (D8): the current location's marker —
            // included as a non-actionable you-are-here row, never on the station.
            bool occupied = map && state == "Occupied";
            if (state == null || (!Listed.Contains(state) && !occupied))
            {
                if (state != null && !Absent.Contains(state) && LoggedStates.Add(state))
                    Plugin.Log.LogWarning("[Atlas] UNLISTED DIAL STATE (excluded): \""
                        + state + "\" on " + root.name);
                return null;
            }

            var nameNode = billboard.Find("Portrait Name");
            string name = nameNode != null ? SpeechService.Clean(TmpOf(nameNode)) : null;
            if (string.IsNullOrEmpty(name)) return null; // no rendered identity

            var marker = billboard.Find("Marker");
            var button = marker != null ? marker.Find("Location Button") : null;
            if (button == null) return null;

            var pos = marker.position;
            var center = map ? null : RigCenter(); // the station rig is not the map's frame

            return new Node
            {
                Name = name,
                State = state,
                Root = root,
                Button = button.gameObject,
                CamFocus = root.Find("Location Contents/Cam Focus"),
                // The map-axis angle: Y/Z plane around the rig center (the hub is
                // a vertical arc — live sweep + marker geometry, 2026-07-31).
                Azimuth = center != null
                    ? Mathf.Atan2(pos.y - center.position.y, pos.z - center.position.z) * Mathf.Rad2Deg
                    : 0f,
                IsHere = occupied,
            };
        }

        /// <summary>The node's spoken read, composed LIVE from the billboard at
        /// speech time (owner ruling 2026-07-26: no snapshot caches — the table's
        /// hover fires first and the game's FSM reaction is synchronous, so the
        /// highlight has already rendered the description when this reads it; the
        /// active-state gate is honest again). Degrades to name-only if a
        /// deferred activation ever appears — still rendered truth.</summary>
        public static string Read(Node node, string newWord)
        {
            var billboard = node.Root != null
                ? node.Root.Find("Location Contents/Billboard Elements") : null;
            if (billboard == null) return node.Name + ".";
            var nameNode = billboard.Find("Portrait Name");
            string name = nameNode != null ? SpeechService.Clean(TmpOf(nameNode)) : null;
            var sb = new System.Text.StringBuilder(
                !string.IsNullOrEmpty(name) ? name : node.Name);
            var descNode = nameNode != null ? nameNode.Find("Description") : null;
            if (descNode != null && descNode.gameObject.activeInHierarchy)
            {
                string desc = SpeechService.Clean(TmpOf(descNode));
                if (!string.IsNullOrEmpty(desc)) sb.Append(". ").Append(desc);
            }
            sb.Append('.');
            var shine = billboard.Find("Marker/Shine Mask/New Shine");
            if (shine != null && shine.gameObject.activeInHierarchy)
                sb.Append(' ').Append(newWord);
            return sb.ToString();
        }

        private static string TmpOf(Transform t)
        {
            var tmp = t.GetComponent<TMP_Text>();
            return tmp != null ? tmp.text : null;
        }

        // ---------- Facet readers (zone-table columns; decodes D1/D2, 2026-07-31) ----------
        // Data, not strings (CS1 map-table architecture ruling): wording composes in
        // the table's own block. All reads are live-at-speech-time — the table's
        // hover has already rendered the billboard when these run.

        internal sealed class NameFacet
        {
            public string Name;
            public bool IsNew;      // Marker/Shine Mask/New Shine rendered
            public bool Disabled;   // Location Button not interactable (CS1 flag ruling)
        }

        internal sealed class ActionsFacet
        {
            public int Cards = -1;  // -1 = no group resolved (cell speaks its empty form)
            public int Unavailable;
        }

        public static NameFacet ReadNameFacet(Node node)
        {
            var facet = new NameFacet { Name = node.Name };
            var billboard = node.Root != null
                ? node.Root.Find("Location Contents/Billboard Elements") : null;
            if (billboard == null) return facet;
            var nameNode = billboard.Find("Portrait Name");
            string live = nameNode != null ? SpeechService.Clean(TmpOf(nameNode)) : null;
            if (!string.IsNullOrEmpty(live)) facet.Name = live;
            var shine = billboard.Find("Marker/Shine Mask/New Shine");
            facet.IsNew = shine != null && shine.gameObject.activeInHierarchy;
            var button = node.Button != null
                ? node.Button.GetComponent<UnityEngine.UI.Button>() : null;
            facet.Disabled = button != null && !button.interactable;
            return facet;
        }

        /// <summary>Is this node's billboard subtree DRAWN? Every facet below —
        /// the New shine, the clock, the drive pips, the action group, the
        /// description — hangs off Billboard Elements, and the game leaves that
        /// subtree deactivated until its camera pan arrives (M14 diagnosis of
        /// record). While it is down, a row report is a snapshot of a node that
        /// has not finished drawing, and every empty facet reads "not yet",
        /// not "none". Render-first: this asks the screen, not a model of it.</summary>
        public static bool BillboardUp(Node node)
        {
            var billboard = node.Root != null
                ? node.Root.Find("Location Contents/Billboard Elements") : null;
            return billboard != null && billboard.gameObject.activeInHierarchy;
        }

        /// <summary>Rendered description line (live). Null when nothing renders.</summary>
        public static string ReadDescription(Node node)
        {
            var billboard = node.Root != null
                ? node.Root.Find("Location Contents/Billboard Elements") : null;
            var nameNode = billboard != null ? billboard.Find("Portrait Name") : null;
            var descNode = nameNode != null ? nameNode.Find("Description") : null;
            if (descNode == null || !descNode.gameObject.activeInHierarchy) return null;
            string desc = SpeechService.Clean(TmpOf(descNode));
            return string.IsNullOrEmpty(desc) ? null : desc;
        }

        /// <summary>Billboard clock facet: presence by the clock FSM CLASS (the
        /// ClockValue carrier — GameQueries.ClockFsm; ride V1 ruling: the class is
        /// the variable, never a name or an inexact state signature). Content =
        /// "x of y" from the clock's own dial, then any text it renders.</summary>
        public static bool ReadClock(Node node, List<string> texts)
        {
            texts.Clear();
            var billboard = node.Root != null
                ? node.Root.Find("Location Contents/Billboard Elements") : null;
            if (billboard == null) return false;
            bool present = false;
            foreach (Transform child in billboard)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                var clockFsm = GameQueries.ClockFsm(child);
                if (clockFsm == null) continue;
                present = true;
                string progress = GameQueries.ClockProgress(clockFsm);
                if (progress != null) texts.Add(progress);
                foreach (var tmp in child.GetComponentsInChildren<TMP_Text>())
                {
                    if (!tmp.gameObject.activeInHierarchy) continue;
                    string t = SpeechService.Clean(tmp.text);
                    if (!string.IsNullOrEmpty(t)) texts.Add(t);
                }
            }
            return present;
        }

        /// <summary>Shown drive pips (decode D1): pip-class children of Billboard
        /// Elements (state set PIP on/PIP off — mechanism class, never instance
        /// names), listed iff rendered — GameObject active (Complete deactivates
        /// permanently) and CanvasGroup alpha up. Identity is the pip's own
        /// Quest Name string (the Dialogue System quest name — the same label the
        /// journal renders as the drive heading, D5). Drives follow render:
        /// untracked or non-Active entries simply are not shown (CS1 ruling 5).</summary>
        public static List<string> ReadDrives(Node node)
        {
            var names = new List<string>();
            var billboard = node.Root != null
                ? node.Root.Find("Location Contents/Billboard Elements") : null;
            if (billboard == null) return names;
            foreach (Transform child in billboard)
            {
                var fsm = FsmWithStates(child, "PIP on", "PIP off");
                if (fsm == null) continue;
                if (!child.gameObject.activeInHierarchy) continue;
                // Effective alpha — own CanvasGroup AND ancestors (sync review LOW:
                // a pip's own group at 1 under a hidden ancestor is not rendered).
                if (!Util.RenderedUp(child)) continue;
                var quest = fsm.FsmVariables.GetFsmString("Quest Name");
                if (quest == null || string.IsNullOrEmpty(quest.Value))
                {
                    LogOnce("[Atlas] pip without Quest Name under " + node.Name);
                    continue;
                }
                if (!names.Contains(quest.Value)) names.Add(quest.Value);
            }
            return names;
        }

        /// <summary>Actions count (decode D2): the marker FSM's own Location Actions
        /// pointer names the group — never name-matched; cards are active-self direct
        /// children owning an FSM with a non-empty Action Identifier (363/363
        /// controllers carry it); the unavailable subset reads the exact Lua variable
        /// the card itself reads before graying ("&lt;id&gt;_COMPLETE"). This is a
        /// re-derivation from the cards' own sources, not an observed render —
        /// documented caveats (stale switches, crisis) in docs/decodes/D2.</summary>
        public static ActionsFacet ReadActions(Node node)
        {
            var facet = new ActionsFacet();
            if (node.Root == null) return facet;
            GameObject group = null;
            foreach (var fsm in node.Root.GetComponents<PlayMakerFSM>())
            {
                var v = fsm.FsmVariables.GetFsmGameObject("Location Actions");
                if (v != null && v.Value != null) { group = v.Value; break; }
            }
            if (group == null)
            {
                LogOnce("[Atlas] no Location Actions pointer on " + node.Root.name);
                return facet;
            }
            facet.Cards = 0;
            foreach (Transform card in group.transform)
            {
                if (!card.gameObject.activeSelf) continue;
                string id = ActionIdentifierOf(card);
                if (id == null)
                {
                    // The End Cycle card is a real, pressable option and counts
                    // toward the row's tally — it just tracks no completion of
                    // its own (there is no <ID>_COMPLETE to ask about).
                    if (IsEndCycleCard(card)) facet.Cards++;
                    continue; // else clocks, notifications, stress meters
                }
                facet.Cards++;
                var complete = LuaStore.Num(id + "_COMPLETE");
                if (complete.HasValue && complete.Value >= 1) facet.Unavailable++;
            }
            return facet;
        }

        /// <summary>Is this card the END CYCLE card? A THIRD card class, beside
        /// the identifier-bearing actions and the clocks (owner report 2026-08-04:
        /// the Abandoned Unit soft-locked — its group holds exactly one card, an
        /// End Cycle, and the row scan admitted neither class, so the view landed
        /// "Nothing here." and every arrow repeated it with the only option on
        /// screen unreachable). The card carries no "Action Identifier", so it
        /// must be recognised by mechanism: its FSM alone owns the supply ladder
        /// that decides what ending a cycle costs. Corpus of record — the state
        /// pair below has exactly 3 carriers game-wide and all 3 ARE this card
        /// (the rig display's and the two contract rest-node copies); the
        /// "EndCycle" EVENT was rejected as the key, since 41 Location Buttons
        /// and the sibling Cycle Controller Relay raise it too.</summary>
        private static bool _endCycleClassLogged;

        internal static bool IsEndCycleCard(Transform card)
        {
            if (card == null) return false;
            if (FsmWithStates(card, "Crew Depletion", "Supplied") == null) return false;
            if (!_endCycleClassLogged)
            {
                _endCycleClassLogged = true;
                // Info, not a warning: this class is understood and expected.
                Plugin.Log.LogInfo("[Atlas] End Cycle card admitted as an action row ("
                    + card.name + ")");
            }
            return true;
        }

        internal static string ActionIdentifierOf(Transform card)
        {
            foreach (var fsm in card.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                // Find*, never Get* (S9 law): GetFsmString AUTO-CREATES the
                // variable, so this existence check was quietly writing an empty
                // "Action Identifier" onto every FSM it inspected. The empty
                // guard below kept the ANSWER right, which is why it went
                // unnoticed — the pollution was the cost.
                var id = fsm.FsmVariables.FindFsmString("Action Identifier");
                if (id != null && !string.IsNullOrEmpty(id.Value)) return id.Value;
            }
            return null;
        }

        /// <summary>First FSM on the child whose state set contains both markers —
        /// mechanism-class matching (universal-hooks rule).</summary>
        private static PlayMakerFSM FsmWithStates(Transform t, string a, string b)
        {
            foreach (var fsm in t.GetComponents<PlayMakerFSM>())
            {
                bool hasA = false, hasB = false;
                var states = fsm.FsmStates;
                if (states == null) continue;
                foreach (var s in states)
                {
                    if (s.Name == a) hasA = true;
                    else if (s.Name == b) hasB = true;
                }
                if (hasA && hasB) return fsm;
            }
            return null;
        }

        private static readonly HashSet<string> LoggedFacets = new HashSet<string>();

        private static void LogOnce(string line)
        {
            if (LoggedFacets.Add(line)) Plugin.Log.LogWarning(line);
        }

        // ---------- Camera ----------
        // Node camera follow is the GAME's job (owner ruling 2026-07-26): the
        // zone table selects the node's button through EventSystem — the dpad
        // idiom — and the game's selection machinery drives its authored
        // per-node Location Cam. The derived var-write drive (Damped X +
        // X Rot Global) is retired from the mod; it remains documented in
        // docs/port-audit.md §7b as a bridge calibration tool.

        private static Transform FocusTransform()
        {
            var go = GameObject.Find("Focus Body/Focus Gimbal/Focus");
            return go != null ? go.transform : null;
        }

        private static Transform RigCenter()
        {
            var go = GameObject.Find("Focus Body");
            return go != null ? go.transform : null;
        }

        /// <summary>The UI selector's Closest UI Button — the proximity-selection
        /// truth the whole surface rides on. GameObject-typed on the FSM (the
        /// first build read it as a string and compared names against paths —
        /// every drive "missed", ride capture 2026-07-26).</summary>
        public static GameObject ClosestButton()
        {
            var focus = FocusTransform();
            if (focus == null) return null;
            var selector = focus.Find("UI selector");
            if (selector == null) return null;
            foreach (var fsm in selector.GetComponents<PlayMakerFSM>())
            {
                var v = fsm.FsmVariables.GetFsmGameObject("Closest UI Button");
                if (v != null) return v.Value;
            }
            return null;
        }
    }
}
