using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Item changes outside an action (2026-10-25): items given in a
    /// conversation or as contract pay were never spoken (only fuel, supplies and
    /// cryo had change lines). The inventory strip's own item slots carry the
    /// drawn count (Amount) and switch on when the first item arrives; a change is
    /// spoken as "RAW SCRAP plus 1, now 2." The lanes that already voice item
    /// changes (an action outcome, a push, the cycle transition) are left alone:
    /// changes during them, and for three seconds after, update silently.</summary>
    internal static class ItemWatch
    {
        private const float PollEvery = 0.25f;
        private const float StandDown = 3f;
        private static readonly string[] Excluded = { "Cryo Slot", "Fuel Slot", "Supplies Slot" };

        private static float _nextPoll;
        private static float _busyAt = -10f;
        private static bool _baselined;
        private static readonly Dictionary<string, int> _last = new Dictionary<string, int>();

        public static void OnSceneChanged()
        {
            _baselined = false;
            _last.Clear();
        }

        public static void Tick()
        {
            if (ActionOutcomes.ResolutionInFlight || PushFlow.FireInFlight
                || CycleGate.TransitionInFlight)
                _busyAt = Time.unscaledTime;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollEvery;

            if (GameQueries.TitleLive()) { OnSceneChanged(); return; }
            var lc = GameObject.Find("Letterbox Canvas");
            var strip = lc != null ? lc.transform.Find("Bottom UI/Inventory/ITEMS Inventory UI") : null;
            if (strip == null) return;

            bool quiet = !_baselined || !FocusPatch.SceneSettled
                || Time.unscaledTime - _busyAt < StandDown;
            foreach (Transform slot in strip)
            {
                string key = slot.name.Trim();
                if (!key.EndsWith(" Slot") || System.Array.IndexOf(Excluded, key) >= 0) continue;
                int now = Count(slot);
                if (!_last.TryGetValue(key, out int before)) { _last[key] = now; continue; }
                if (now == before) continue;
                _last[key] = now;
                if (quiet) continue;
                int delta = now - before;
                string line = ItemName(key) + " " + Lex.T(delta > 0 ? "items.plus" : "items.minus")
                    + " " + Mathf.Abs(delta) + ", " + Lex.T("items.now") + " " + now + ".";
                Plugin.Log.LogInfo("[Items] " + line);
                SpeechService.Say(line, Priority.Queued, "items");
            }
            _baselined = true;
        }

        private static int Count(Transform slot)
        {
            if (!slot.gameObject.activeSelf) return 0;
            var amount = slot.Find("Amount");
            var tmp = amount != null ? amount.GetComponent<TMPro.TMP_Text>() : null;
            return tmp != null && int.TryParse(tmp.text.Trim(), out int n) ? n : 0;
        }

        /// <summary>The slot's object name as the item's name, with the game's own
        /// spelling slips mended (\"Stablizer\", the missing apostrophes).</summary>
        private static string ItemName(string slotName)
        {
            string name = slotName.Substring(0, slotName.Length - " Slot".Length).Trim();
            switch (name)
            {
                case "Stablizer": name = "Stabilizer"; break;
                case "Serafins Coin": name = "Serafin's Coin"; break;
                case "Fengs Gum Box": name = "Feng's Gum Box"; break;
            }
            return name.ToUpperInvariant();
        }
    }
}
