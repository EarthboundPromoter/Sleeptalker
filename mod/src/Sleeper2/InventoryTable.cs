using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The inventory strip table (Phase 5 close; decode D6, owner
    /// rulings 2026-08-02).
    ///
    /// Rows = the slot GOs the game currently renders under ITEMS Inventory UI,
    /// transform child order (active-only — an empty slot has NO render
    /// existence: its manager deactivates it at count 0; cryo alone never
    /// hides). The strip INITIATES nothing: action cards auto-grab their item
    /// natively (the D11 dice-slot contract), so there is no use path here —
    /// this is a review surface.
    ///
    /// NAV IS LATERAL (owner ruling 2026-08-02): Left/Right walk the items —
    /// the strip is a horizontal row of cells and the walk mirrors the screen.
    /// Up/Down are swallowed (fence). Space/Enter read the description. I or
    /// Backspace close; M/G/U SWAP surfaces (InputManager auto-closes the
    /// strip first — same ruling).
    ///
    /// Mode = the game's OWN browse mode (root FSM state Item 5): I toggles it
    /// by sending the same Activate/Deactivate events the native Rewired
    /// Inventory Toggle sends. Inspection rides NATIVE selection (owner
    /// ruling): arrival selects the slot's Item Cursor — the game's own Select
    /// handler renders name + description into the Inventory Display tooltip,
    /// and both are SNAPSHOTTED in that synchronous beat (sync pass 2026-08-02
    /// F2/F3: the tooltip Disappears ~0.4s later and a failed select leaves
    /// the previous row's render standing — all reads speak the snapshot).
    ///
    /// THE FENCE (owner law, CS1 Item Cursor lineage): while browse mode is up
    /// the mod owns the keys and only ever moves selection between Item Cursor
    /// GOs — the root FSM's watchdog Deactivates the whole mode the moment
    /// selection leaves the strip. A native/watchdog exit is announced from
    /// the mode dial itself, so the table never believes in a dead mode.</summary>
    internal static class InventoryTable
    {
        private const float CacheWindow = 0.4f;

        private static List<Transform> _rows = new List<Transform>();
        private static float _builtAt = -1f;
        private static bool _entered;
        private static int _cursor;

        /// <summary>Select-echo suppression: inside the fence EVERY cursor
        /// selection is either mod-driven or the entry race the game runs when
        /// browse mode opens (all cursors' On states race SetSelected) — never
        /// news. Unconditional while browsing (sync pass 2026-08-02 F5).</summary>
        public static bool SuppressCursorFocus(GameObject go)
            => go != null && go.name == "Item Cursor" && GameQueries.InventoryBrowse();

        public static void Init() { }

        public static bool Active() => ModeModel.Current() == Mode.Inventory;

        /// <summary>Lateral grammar (owner ruling): Left/Right walk, Up/Down
        /// swallowed, Space/Enter = description. Backspace falls through to
        /// ResolveCancel's Inventory rung; M/G/U fall through to the swap
        /// handlers. Edge presses re-read the end item (no wrap — the strip
        /// has ends on screen).</summary>
        public static bool HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.RightArrow)) { Move(1); return true; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { Move(-1); return true; }
            if (Input.GetKeyDown(KeyCode.UpArrow)
                || Input.GetKeyDown(KeyCode.DownArrow)) return true; // fence
            if (Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Say(DetailRead(_cursor));
                return true;
            }
            return false;
        }

        private static void Move(int delta)
        {
            var rows = Rows();
            if (rows.Count == 0) { Say(Lex.T("inv.empty")); return; }
            int target = Mathf.Clamp(_cursor + delta, 0, rows.Count - 1);
            _cursor = target;
            ArriveRow(target);
            Say(RowRead(target));
        }

        /// <summary>Entry/exit ride the game's OWN mode dial, so native exits
        /// (the watchdog, the native toggle, a window parking the root) and
        /// mod exits all land in one place.</summary>
        public static void Tick()
        {
            GameQueries.InventoryPendingTick();
            bool browse = GameQueries.InventoryBrowse();
            if (browse && !_entered)
            {
                _entered = true;
                _builtAt = -1f;
                _cursor = RowOfSelected();
                // Arrival work runs for the entry row too: OUR select feeds the
                // watchdog (the native race may not have landed a cursor —
                // Cooldown beat, sync pass F6) and takes the tooltip snapshot
                // the announce speaks from.
                ArriveRow(_cursor);
                Say(Lex.T("inv.title") + " " + RowRead(_cursor), Priority.Queued);
            }
            else if (!browse && _entered)
            {
                _entered = false;
                _builtAt = -1f;
                _cursor = 0;
                _snapRow = -1;
                _snapName = _snapDesc = null;
                // QUEUED, never Immediate (ride finding 2026-08-02: on a U swap
                // the character window's own open announce is already in the
                // queue — an Immediate close line flushed it; utterances queue
                // one after another, owner law).
                Say(Lex.T("inv.closed"), Priority.Queued);
            }
        }

        public static void OnSceneChanged()
        {
            _entered = false;
            _builtAt = -1f;
            _cursor = 0;
            _snapRow = -1;
            _snapName = _snapDesc = null;
        }

        private static void Say(string line, Priority priority = Priority.Immediate)
            => SpeechService.Say(line, priority, "inventory");

        // ---------- Arrival: native select + tooltip snapshot ----------
        // Sync pass 2026-08-02 F2/F3: the tooltip is only trustworthy in the
        // synchronous beat after OUR OWN Select fired the Highlight render —
        // ~0.4s later the outgoing drag FSM's reset pipeline Disappears it,
        // and a row whose cursor could not be selected leaves the PREVIOUS
        // row's render standing. So name + description are snapshotted at
        // arrival (the capture-from-highlight law, applied literally) and all
        // later reads speak the snapshot; a row that never got its Select
        // falls to the slot-name fallback and NO description.

        private static int _snapRow = -1;
        private static string _snapName, _snapDesc;

        private static void ArriveRow(int row)
        {
            _snapRow = row;
            _snapName = null;
            _snapDesc = null;
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return;
            var slot = rows[row];
            bool landed = false;
            var cursor = CursorOf(slot);
            if (cursor == null)
            {
                LogOnce("[Inventory] slot \"" + slot.name.TrimEnd()
                    + "\" has no Item Cursor — selection not moved");
            }
            else
            {
                // Native inspection: Select fires the game's own Highlight →
                // tooltip render, synchronously. Fence-safe by construction:
                // the target IS an Item Cursor, so the watchdog stays fed —
                // and a mod-side Select keeps the mode alive even through the
                // cursors' 0.2s Cooldown beat (sync pass F6).
                var es = EventSystem.current;
                if (es != null)
                {
                    if (es.currentSelectedGameObject != cursor)
                        es.SetSelectedGameObject(cursor);
                    landed = es.currentSelectedGameObject == cursor;
                }
            }
            if (landed)
            {
                _snapName = TooltipText("Item Name");
                _snapDesc = TooltipText("Item Description");
            }
            if (_snapName == null)
            {
                _snapName = SlotFallbackName(slot);
                if (landed)
                    LogOnce("[Inventory] tooltip rendered no name for \""
                        + slot.name.TrimEnd() + "\" — GO-name fallback spoken");
            }
        }

        private static string SlotFallbackName(Transform slot)
        {
            string name = slot.name.TrimEnd();
            return name.EndsWith(" Slot") ? name.Substring(0, name.Length - 5) : name;
        }

        // ---------- Rows ----------

        private static Transform StripRoot()
        {
            var fsm = GameQueries.InventoryRootFsm();
            var t = fsm != null ? fsm.transform.Find("ITEMS Inventory UI") : null;
            return t != null && t.gameObject.activeInHierarchy ? t : null;
        }

        /// <summary>Active slot GOs in transform order — the slot class is the
        /// " Slot" name suffix (TrimEnd covers the "Cryo Slot " trailing-space
        /// wart), which also excludes the gamepad prompt chrome.</summary>
        private static List<Transform> Rows()
        {
            if (Time.unscaledTime - _builtAt <= CacheWindow) return _rows;
            _builtAt = Time.unscaledTime;
            _rows = new List<Transform>();
            var root = StripRoot();
            if (root == null) return _rows;
            foreach (Transform slot in root)
            {
                if (!slot.gameObject.activeInHierarchy) continue;
                if (!slot.name.TrimEnd().EndsWith(" Slot")) continue;
                _rows.Add(slot);
            }
            return _rows;
        }

        private static GameObject CursorOf(Transform slot)
        {
            foreach (var t in slot.GetComponentsInChildren<Transform>(false))
                if (t.name == "Item Cursor") return t.gameObject;
            return null;
        }

        private static int RowOfSelected()
        {
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected == null) return 0;
            var rows = Rows();
            for (int i = 0; i < rows.Count; i++)
            {
                var cursor = CursorOf(rows[i]);
                if (cursor != null && cursor == selected) return i;
            }
            return 0;
        }

        // ---------- Speech (snapshot name/desc; live count render) ----------

        /// <summary>"NAME. 3 of 5." — name from the arrival SNAPSHOT (the
        /// tooltip render our own Select produced), count from the slot's
        /// Amount TMP live (the slot's own text has no staleness), capacity
        /// only where a Capacity TMP is drawn (fuel/supplies; the other 14
        /// are inactive vestiges, D6).</summary>
        private static string RowRead(int row)
        {
            var rows = Rows();
            if (row < 0 || row >= rows.Count) return Lex.T("inv.empty");
            var slot = rows[row];

            if (row != _snapRow) ArriveRow(row); // defensive: never a stale snapshot
            string name = _snapName ?? SlotFallbackName(slot);

            var sb = new System.Text.StringBuilder(name).Append('.');
            string count = SlotText(slot, "Amount");
            if (count != null)
            {
                sb.Append(' ').Append(count);
                string cap = SlotText(slot, "Capacity");
                if (cap != null)
                    sb.Append(' ').Append(Lex.T("vitals.of")).Append(' ').Append(cap);
                sb.Append('.');
            }
            else
            {
                LogOnce("[Inventory] slot \"" + slot.name.TrimEnd()
                    + "\" renders no Amount text — count silent");
            }
            return sb.ToString();
        }

        private static string DetailRead(int row)
        {
            string read = RowRead(row); // refreshes the snapshot if rows shifted
            return _snapDesc != null ? read + " " + _snapDesc : read;
        }

        /// <summary>A rendered TMP directly under the named slot child. The
        /// Capacity child renders the live limit via its own FSM; absent or
        /// undrawn = null (existence-based).</summary>
        private static string SlotText(Transform slot, string childName)
        {
            var child = slot.Find(childName);
            if (child == null || !child.gameObject.activeInHierarchy) return null;
            var tmp = child.GetComponent<TMP_Text>();
            if (tmp == null || !Util.RenderedUp(tmp.transform)) return null;
            string text = SpeechService.Clean(tmp.text);
            if (string.IsNullOrEmpty(text)) return null;
            // Capacity draws as "/ N" fragments in some layouts — speak digits.
            float n = Util.LeadingInt(text.TrimStart('/', ' '));
            return n > 0f || text.Trim() == "0"
                ? n.ToString("0") : text;
        }

        /// <summary>The Inventory Display tooltip's rendered text — the game's
        /// own inspection render, populated by cursor Select (D6 (f)).</summary>
        private static string TooltipText(string childName)
        {
            var fsm = GameQueries.InventoryRootFsm();
            var display = fsm != null ? fsm.transform.Find("Inventory Display") : null;
            if (display == null || !display.gameObject.activeInHierarchy) return null;
            var child = display.Find(childName);
            if (child == null) return null;
            var tmp = child.GetComponent<TMP_Text>();
            if (tmp == null || !Util.RenderedUp(tmp.transform)) return null;
            string text = SpeechService.Clean(tmp.text);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void LogOnce(string line)
        {
            if (Logged.Add(line)) Plugin.Log.LogWarning(line);
        }
    }
}
