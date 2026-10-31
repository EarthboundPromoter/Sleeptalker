using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The notification feed (2026-10-25; the Citizen Speaker watcher,
    /// ported): the game's Character Notifications canvas draws a short card for
    /// NEW DRIVE AVAILABLE, MAJOR/MINOR DRIVE COMPLETE, DRIVE FAILED, upgrade
    /// points, CONTRACT COMPLETE/FAILED, NEW CONTRACT ON MAP, NEW LOCATION
    /// DISCOVERED, NEW CRISIS LOCATION ACTIVE and new crew. None of it was
    /// spoken. A card is spoken once, from its own rendered text, when it starts
    /// to show; cards already showing when the canvas is first found stay quiet
    /// (load catch-up, the boot-sweep rule).</summary>
    internal static class NotificationWatch
    {
        private const string RootPath = "Letterbox Canvas/Character Notifications";
        private const float PollEvery = 0.15f;
        private const float FindEvery = 1f;

        private static Transform _root;
        private static float _nextPoll;
        private static float _nextFind;
        private static bool _baselined;
        private static readonly HashSet<int> _visible = new HashSet<int>();
        private static readonly List<Transform> _cards = new List<Transform>();

        public static void OnSceneChanged()
        {
            _root = null;
            _baselined = false;
            _visible.Clear();
            _nextFind = 0f;
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollEvery;
            if (_root == null)
            {
                if (Time.unscaledTime < _nextFind) return;
                _nextFind = Time.unscaledTime + FindEvery;
                var go = GameObject.Find(RootPath);
                if (go == null) return;
                _root = go.transform;
                _baselined = false;
                _visible.Clear();
                Plugin.Log.LogInfo("[Notify] feed found: " + Util.PathOf(go));
            }

            Cards(_root, _cards);
            foreach (var card in _cards)
            {
                int id = card.GetInstanceID();
                bool showing = card.gameObject.activeInHierarchy
                    && Util.AlphaUpTo(card, _root.parent) > 0.5f;
                if (!showing) { _visible.Remove(id); continue; }
                if (!_visible.Add(id) || !_baselined) continue;
                string text = CardText(card);
                if (text == null)
                {
                    // Drawn before its text: try again next poll.
                    _visible.Remove(id);
                    continue;
                }
                Plugin.Log.LogInfo("[Notify] " + card.name + ": " + text);
                SpeechService.Say(text, Priority.Queued, "notify");
            }
            _baselined = true;
        }

        /// <summary>The cards: the canvas groups its cards by kind one level down
        /// (the Citizen Sleeper 1 shape); a child with no children of its own is
        /// a card itself.</summary>
        private static void Cards(Transform root, List<Transform> into)
        {
            into.Clear();
            foreach (Transform group in root)
            {
                if (group.childCount == 0) { into.Add(group); continue; }
                foreach (Transform card in group) into.Add(card);
            }
        }

        /// <summary>The card's drawn text in reading order (top to bottom, then
        /// left to right), as one sentence per text element.</summary>
        private static string CardText(Transform card)
        {
            var texts = new List<TMPro.TMP_Text>();
            foreach (var tmp in card.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                if (string.IsNullOrEmpty(SpeechService.Clean(tmp.text))) continue;
                texts.Add(tmp);
            }
            if (texts.Count == 0) return null;
            // Rows of 10 units, top row first, then left to right (a consistent
            // key, so the sort is well-defined).
            texts.Sort((a, b) =>
            {
                int ra = Mathf.RoundToInt(a.transform.position.y / 10f);
                int rb = Mathf.RoundToInt(b.transform.position.y / 10f);
                if (ra != rb) return rb.CompareTo(ra);
                return a.transform.position.x.CompareTo(b.transform.position.x);
            });
            var sb = new System.Text.StringBuilder();
            foreach (var tmp in texts)
            {
                string t = SpeechService.Clean(tmp.text);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
                char last = t[t.Length - 1];
                if (last != '.' && last != '!' && last != '?') sb.Append('.');
            }
            return sb.ToString();
        }
    }
}
