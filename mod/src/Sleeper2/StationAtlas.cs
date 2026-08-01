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
        }

        // CS1 baseline whitelist; CS2 states join per capture (unknowns log).
        private static readonly HashSet<string> Listed = new HashSet<string>
        {
            "Variables Met", "Off Camera", "Selected", "Cycle Check",
            "Clock Flasher", "Flashed Already?",
        };
        private static readonly HashSet<string> LoggedStates = new HashSet<string>();

        private static readonly List<Transform> Containers = new List<Transform>();
        private static bool _containersFresh;

        public static void InvalidateScene() => _containersFresh = false;

        /// <summary>Active "Locations" containers holding Location Buttons — scanned
        /// once per scene (hub confirmed live; contract/rig containers join per
        /// capture, which this scan discovers and logs).</summary>
        private static List<Transform> FindContainers()
        {
            if (_containersFresh) return Containers;
            Containers.Clear();
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (t.name != "Locations") continue;
                if (t.GetComponentInChildren<UnityEngine.UI.Button>(true) == null) continue;
                Containers.Add(t);
                Plugin.Log.LogInfo("[Atlas] container: " + Util.PathOf(t.gameObject));
            }
            _containersFresh = true;
            return Containers;
        }

        /// <summary>Fresh node list, azimuth-sorted (walking Down travels one way
        /// around the station). Unlisted dial states are excluded and logged.</summary>
        public static List<Node> Build()
        {
            var nodes = new List<Node>();
            foreach (var container in FindContainers())
            {
                if (container == null || !container.gameObject.activeInHierarchy) continue;
                foreach (Transform root in container)
                {
                    var node = NodeOf(root);
                    if (node != null) nodes.Add(node);
                }
            }
            nodes.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));
            return nodes;
        }

        private static Node NodeOf(Transform root)
        {
            var fsm = root.GetComponent<PlayMakerFSM>();
            string state = fsm != null ? fsm.ActiveStateName : null;
            var billboard = root.Find("Location Contents/Billboard Elements");
            if (billboard == null) return null; // not a location node
            if (state == null || !Listed.Contains(state))
            {
                if (state != null && LoggedStates.Add(state))
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
            var center = RigCenter();

            return new Node
            {
                Name = name,
                State = state,
                Root = root,
                Button = button.gameObject,
                CamFocus = root.Find("Location Contents/Cam Focus"),
                Azimuth = center != null
                    ? Mathf.Atan2(pos.x - center.position.x, pos.z - center.position.z) * Mathf.Rad2Deg
                    : 0f,
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
                var cg = child.GetComponent<CanvasGroup>();
                float alpha = cg != null ? cg.alpha : Util.AlphaUpTo(child);
                if (alpha < 0.05f) continue;
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
                if (id == null) continue; // clocks, notifications, stress meters
                facet.Cards++;
                var complete = LuaStore.Num(id + "_COMPLETE");
                if (complete.HasValue && complete.Value >= 1) facet.Unavailable++;
            }
            return facet;
        }

        internal static string ActionIdentifierOf(Transform card)
        {
            foreach (var fsm in card.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                var id = fsm.FsmVariables.GetFsmString("Action Identifier");
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
