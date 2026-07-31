using System.Collections.Generic;
using TMPro;
using UnityEngine;

using Sleeptalker.Scaffold;

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
