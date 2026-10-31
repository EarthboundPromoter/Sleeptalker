using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Keeps the keyboard on a live conversation (ride 2026-10-30): a
    /// conversation that starts while the Drive Log is open (Kadet at Bliss's
    /// Bay) leaves the game's selection on the log's quest buttons. Arrows then
    /// walked "MAKE REPAIRS" and Enter did nothing; only the number keys still
    /// answered. When the selection sits outside the dialogue UI, this finds the
    /// selectable the conversation is waiting on: the first response while the
    /// response menu is up, else the CONTINUE button.</summary>
    internal static class DialogueAnchor
    {
        private const string DialogueRoot = "CS Dialogue Manager";

        /// <summary>The dialogue selectable to move to, or null when the selection
        /// is already in the dialogue UI or the pause menu (paused stays paused).</summary>
        public static GameObject Needed()
        {
            var es = EventSystem.current;
            var current = es != null ? es.currentSelectedGameObject : null;
            if (current != null && current.activeInHierarchy
                && (Util.HasAncestor(current, DialogueRoot) || Util.HasAncestor(current, "Pause Canvas")))
                return null;
            return Target();
        }

        private static GameObject Target()
        {
            var root = GameObject.Find(DialogueRoot);
            if (root == null) return null;
            GameObject firstResponse = null, cont = null, any = null;
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                if (!b.IsInteractable() || !Util.RenderedUp(b.transform)) continue;
                var go = b.gameObject;
                if (go.name.StartsWith("Response")) { if (firstResponse == null) firstResponse = go; continue; }
                string label = Describe.FirstText(go);
                if (cont == null && ((label != null && label.ToUpperInvariant().Contains("CONTINUE"))
                                     || go.name.ToUpperInvariant().Contains("CONTINUE")))
                    cont = go;
                if (any == null) any = go;
            }
            if (DialogueState.MenuOpen && firstResponse != null) return firstResponse;
            return cont ?? firstResponse ?? any;
        }

        /// <summary>The Drive Log window is open (its button FSM rests in Open).</summary>
        public static bool DriveLogOpen()
        {
            var go = GameObject.Find("Letterbox Canvas/Drive System/Drive Log Button");
            var fsm = go != null ? go.GetComponent<PlayMakerFSM>() : null;
            return fsm != null && fsm.ActiveStateName == "Open";
        }
    }
}
