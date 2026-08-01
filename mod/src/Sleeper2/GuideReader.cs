using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Guide menu reader (owner design 2026-08-01, on decode D7 (d)):
    /// a two-pane node graph — the topic buttons on the left, the live text
    /// panel on the right. Up/Down walk topics; Enter is the NATIVE click (the
    /// game's own radio-group collapses the open page and activates the
    /// topic's static Tutorial panel) and the cursor jumps into the fresh page
    /// at the top; in the box, Up/Down move by paragraph, Left returns to the
    /// topic list, Space reads the whole page, Right re-enters the open page.
    ///
    /// The box is never a copied buffer: paragraphs are the ACTIVE panel's
    /// rendered TMP blocks, read live in hierarchy order, split by line —
    /// repopulation-to-top is free because nothing is cached. Locked topics
    /// (unlock-gated on tutorial flags, D7) render non-interactable and list
    /// with the disabled flag; Enter on them restates instead of clicking.
    /// The Back row is a plain native click; Esc keeps its native one-level
    /// back-out throughout. The radio fan-out is D7 MEDIUM confidence: a click
    /// whose page fails to repopulate logs loudly and never fakes a page.</summary>
    internal static class GuideReader
    {
        private static GameObject _panel;
        private static bool _wasActive;
        private static int _row;
        private static bool _inBox;
        private static int _para;
        private static Transform _pendingTopic;
        private static float _pendingAt = -1f;

        public static bool IsActive()
        {
            if (_panel == null || !_panel.activeInHierarchy)
                _panel = GameObject.Find("PAUSE/Pause Canvas/Guide Menu");
            bool active = _panel != null && _panel.activeInHierarchy;
            if (active && !_wasActive)
            {
                _row = 0;
                _inBox = false;
                _para = 0;
                _pendingTopic = null;
                SpeechService.Say(Lex.T("guide.intro"), Priority.Queued, "nav");
            }
            if (!active) _pendingTopic = null;
            _wasActive = active;
            return active;
        }

        /// <summary>Deferred box entry after a topic click: the radio-group
        /// collapse/activate runs on the game's own event bus — check after a
        /// beat, enter the page at the top, or log the D7-MED fan-out miss.</summary>
        public static void Tick()
        {
            if (_pendingTopic == null || Time.unscaledTime < _pendingAt) return;
            var topic = _pendingTopic;
            _pendingTopic = null;
            var page = topic.Find("Tutorial");
            if (page != null && page.gameObject.activeInHierarchy
                && Util.RenderedUp(page))
            {
                _inBox = true;
                _para = 0;
                SpeakParagraph(topic);
            }
            else
            {
                Plugin.Log.LogWarning("[Guide] page did not repopulate after \""
                    + topic.name + "\" — capture (radio fan-out, D7 MED)");
                SpeechService.Say(TopicLabel(topic) + Lex.T("topbar.button-suffix"),
                    Priority.Immediate, "nav");
            }
        }

        // ---------- Keys ----------

        public static void Move(int direction)
        {
            if (_inBox)
            {
                var open = OpenTopic();
                if (open == null) { LeaveBox(); return; }
                var paragraphs = Paragraphs(open);
                if (paragraphs.Count == 0)
                {
                    SpeechService.Say(Lex.T("guide.page-empty"), Priority.Immediate, "nav");
                    return;
                }
                _para = Mathf.Clamp(_para + direction, 0, paragraphs.Count - 1);
                SpeechService.Say(paragraphs[_para], Priority.Immediate, "nav");
                return;
            }
            var topics = Topics();
            if (topics.Count == 0)
            {
                SpeechService.Say(Lex.T("guide.empty"), Priority.Immediate, "nav");
                return;
            }
            _row = Mathf.Clamp(_row + direction, 0, topics.Count - 1);
            SpeakTopic(topics[_row]);
        }

        /// <summary>Left: from the box back to the topic list, cursor on the
        /// open topic; on the list, restate (a press is never silent).</summary>
        public static void LeftKey()
        {
            if (_inBox) { LeaveBox(); return; }
            var topics = Topics();
            if (topics.Count == 0)
            { SpeechService.Say(Lex.T("guide.empty"), Priority.Immediate, "nav"); return; }
            _row = Mathf.Clamp(_row, 0, topics.Count - 1);
            SpeakTopic(topics[_row]);
        }

        /// <summary>Right: from the list into the current row's page when it is
        /// the open one (top of text); elsewhere restate.</summary>
        public static void RightKey()
        {
            if (_inBox) { Move(0); return; }
            var topics = Topics();
            if (topics.Count == 0)
            { SpeechService.Say(Lex.T("guide.empty"), Priority.Immediate, "nav"); return; }
            _row = Mathf.Clamp(_row, 0, topics.Count - 1);
            var topic = topics[_row];
            var page = topic.Find("Tutorial");
            if (page != null && page.gameObject.activeInHierarchy
                && Util.RenderedUp(page))
            {
                _inBox = true;
                _para = 0;
                SpeakParagraph(topic);
                return;
            }
            SpeakTopic(topic);
        }

        /// <summary>Space: in the box, the whole page as one utterance (the
        /// table grammar's full report); on the list, restate the row.</summary>
        public static void SpaceKey()
        {
            if (_inBox)
            {
                var open = OpenTopic();
                if (open == null) { LeaveBox(); return; }
                var paragraphs = Paragraphs(open);
                if (paragraphs.Count == 0)
                { SpeechService.Say(Lex.T("guide.page-empty"), Priority.Immediate, "nav"); return; }
                SpeechService.Say(string.Join(" ", paragraphs.ToArray()),
                    Priority.Immediate, "nav");
                return;
            }
            LeftKey();
        }

        public static bool Activate()
        {
            if (_inBox) { Move(0); return true; }
            var topics = Topics();
            if (topics.Count == 0) return false;
            _row = Mathf.Clamp(_row, 0, topics.Count - 1);
            var topic = topics[_row];
            var button = topic.GetComponent<Button>();
            if (button == null || !button.IsInteractable())
            {
                SpeakTopic(topic); // locked: the flag is the answer, no dead click
                return true;
            }
            if (topic.Find("Tutorial") == null)
            {
                Navigator.Click(topic.gameObject); // the Back row — native exit
                return true;
            }
            Navigator.Click(topic.gameObject);     // native radio-group repopulate
            _pendingTopic = topic;
            _pendingAt = Time.unscaledTime + 0.3f;
            return true;
        }

        private static void LeaveBox()
        {
            _inBox = false;
            var topics = Topics();
            var open = OpenTopic();
            if (open != null)
            {
                int i = topics.IndexOf(open);
                if (i >= 0) _row = i;
            }
            if (topics.Count == 0)
            { SpeechService.Say(Lex.T("guide.empty"), Priority.Immediate, "nav"); return; }
            _row = Mathf.Clamp(_row, 0, topics.Count - 1);
            SpeakTopic(topics[_row]);
        }

        // ---------- Structure (D7 (d): Buttons group children; each topic
        // carries its static Tutorial panel child; Back has none) ----------

        private static List<Transform> Topics()
        {
            var topics = new List<Transform>();
            if (_panel == null) return topics;
            var group = _panel.transform.Find("Buttons");
            if (group == null) return topics;
            foreach (Transform child in group)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                if (child.GetComponent<Button>() == null) continue;
                if (!Util.RenderedUp(child)) continue;
                topics.Add(child);
            }
            return topics;
        }

        private static Transform OpenTopic()
        {
            foreach (var topic in Topics())
            {
                var page = topic.Find("Tutorial");
                if (page != null && page.gameObject.activeInHierarchy
                    && Util.RenderedUp(page)) return topic;
            }
            return null;
        }

        /// <summary>The topic's rendered label — the first rendered TMP NOT
        /// inside its Tutorial page child (page text must never masquerade as
        /// the row label while the page is open).</summary>
        private static string TopicLabel(Transform topic)
        {
            foreach (var tmp in topic.GetComponentsInChildren<TMP_Text>(false))
            {
                bool inPage = false;
                for (var cur = tmp.transform; cur != null && cur != topic; cur = cur.parent)
                    if (cur.name == "Tutorial") { inPage = true; break; }
                if (inPage) continue;
                string text = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return topic.name;
        }

        private static void SpeakTopic(Transform topic)
        {
            var button = topic.GetComponent<Button>();
            string speech = TopicLabel(topic) + Lex.T("topbar.button-suffix");
            if (button != null && !button.IsInteractable())
                speech += " " + Lex.T("zone.disabled");
            SpeechService.Say(speech, Priority.Immediate, "nav");
        }

        /// <summary>The open page's paragraphs, live from render: every rendered
        /// TMP under the Tutorial child in hierarchy order, split by line —
        /// locale-variant blocks (Dice Set ja/zh) drop out via the render gate.</summary>
        private static List<string> Paragraphs(Transform topic)
        {
            var paragraphs = new List<string>();
            var page = topic.Find("Tutorial");
            if (page == null) return paragraphs;
            foreach (var tmp in page.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                if (tmp.text == null) continue;
                foreach (var raw in tmp.text.Split('\n'))
                {
                    string line = SpeechService.Clean(raw.TrimStart(' ', '-'));
                    if (!string.IsNullOrEmpty(line)) paragraphs.Add(line);
                }
            }
            return paragraphs;
        }

        private static void SpeakParagraph(Transform topic)
        {
            var paragraphs = Paragraphs(topic);
            if (paragraphs.Count == 0)
            {
                SpeechService.Say(Lex.T("guide.page-empty"), Priority.Immediate, "nav");
                return;
            }
            _para = Mathf.Clamp(_para, 0, paragraphs.Count - 1);
            SpeechService.Say(paragraphs[_para], Priority.Immediate, "nav");
        }
    }
}
