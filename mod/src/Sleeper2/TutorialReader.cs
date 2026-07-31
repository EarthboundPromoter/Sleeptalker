using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Tutorial surfaces, rebuilt on the universal mechanism (owner process
    /// rule 2026-07-26 after the name-keyed first build went silent on its second
    /// panel; CS1's F4 tutorial machinery is the baseline, improved not copied).
    ///
    /// Lifecycle dial: all ~30 Tutorial System trigger FSMs share one shape —
    /// Trigger -> Tutorial -> Complete with a "Tutorial Content" GameObject variable
    /// pointing at the panel they show (census-verified). We subscribe to "Tutorial"
    /// state entry and adopt that panel when it becomes visible; focus-in detection
    /// stays only as a fallback for any modal no trigger announces. Content is
    /// harvested structurally — every visible rendered text in the panel subtree in
    /// hierarchy order, no child names — so panel-internal naming can never silence
    /// a tutorial again (worst case: odd ordering, still audible).
    ///
    /// Prompt transcode (B2 ruling, CS1 2026-07-21, owner-confirmed for CS2
    /// 2026-07-26): prompt glyphs speak the MOD's keys, never gamepad vocabulary —
    /// the game only believes a controller is present because the mod holds that
    /// flag, so its prompts can never be right for this player. CS2 render evidence:
    /// prompts are sibling child objects positioned into literal whitespace gaps in
    /// the text ("Select locations with [gap] and [gap]."). Gap-fill improves on
    /// CS1: prompts sort by on-screen reading order (not raw child order), and the
    /// word resolves mod-key map -> the prompt's own rendered text (LS/RS until
    /// those controls get mod bindings) -> loud-logged placeholder. In-text sprite
    /// tags keep the existing transcode seam. Per-locale position targets
    /// (French/Japanese/Chinese ... Target) are skipped, CS1 "postioner" lineage.
    ///
    /// The buffer is walkable single-column TableEngine as before: Up/Down blocks,
    /// Left/Right repeat, Enter always fires CONTINUE via the pointer path with a
    /// spoken verify; a modal that stops showing clears the buffer and hands focus
    /// to whatever it hid.</summary>
    internal static class TutorialReader
    {
        private static readonly TableEngine Table = new TableEngine
        {
            Rows = () => _blocks.Count,
            Cols = _ => 1,
            RowSpeech = (r, c) => _blocks[r],
            CellSpeech = (r, c) => _blocks[r],
            EmptyRow = () => "No tutorial text.",
            EmptyCol = () => "No tutorial text.",
            Source = "tutorial",
        };

        private static readonly List<string> _blocks = new List<string>();
        private static int _panelId;
        private static Transform _panel;         // the tracked live modal
        private static Transform _pending;       // trigger-announced, not yet visible
        private static float _pendingSince;
        private static float _verifyAt = -1f;    // pending dismissal verification
        private static float _lastDismiss = -1f;

        public static void Init()
        {
            // The universal dial: every tutorial trigger enters "Tutorial" to show
            // its panel, and names that panel in its own Tutorial Content variable.
            FsmSignals.Subscribe(null, "Tutorial", OnTriggerTutorial);
        }

        private static void OnTriggerTutorial(PlayMakerFSM fsm, string state)
        {
            if (fsm == null || !Util.HasAncestor(fsm.gameObject, "Tutorial System")) return;
            var content = fsm.FsmVariables.GetFsmGameObject("Tutorial Content");
            if (content == null || content.Value == null)
            {
                Plugin.Log.LogWarning("[Tutorial] trigger " + fsm.gameObject.name
                    + " entered Tutorial with no Tutorial Content — capture needed.");
                return;
            }
            Diag.Note("Tutorial", "trigger " + fsm.gameObject.name + " -> " + content.Value.name);
            _pending = content.Value.transform;
            _pendingSince = Time.unscaledTime;
            // Claim the speech gate at trigger time, not adoption: the panel takes
            // frames to become visible (variant switch), and chatter that speaks in
            // that window leaks through then gets stomped mid-word by the box read
            // (ride capture 2026-07-26). Claiming here silences the floor once and
            // diverts everything until the box reads. Released on timeout below.
            SpeechService.BeginModal("tutorial");
        }

        /// <summary>Focus inside the Tutorial System = the modal owns the surface.</summary>
        public static bool Up()
        {
            var focused = Navigator.Current();
            return focused != null && Util.HasAncestor(focused, "Tutorial System");
        }

        /// <summary>The modal owns the input surface while a tracked panel is visibly
        /// up, wherever focus sits (owner ruling after the stale-buffer trap: Enter
        /// must always reach CONTINUE, arrows must always reach the buffer).</summary>
        public static bool Active() => PanelVisible() || Up();

        private static bool PanelVisible() => IsShowing(_panel);

        private static bool IsShowing(Transform t)
        {
            return t != null && t.gameObject.activeInHierarchy && Util.AlphaUpTo(t) >= 0.05f;
        }

        public static void Tick()
        {
            // Trigger-announced panel: adopt once it actually shows (the panel's own
            // FSM switches platform variants for a beat after the trigger fires).
            if (_pending != null)
            {
                if (IsShowing(_pending))
                {
                    Adopt(_pending);
                    _pending = null;
                }
                else if (Time.unscaledTime - _pendingSince > 3f)
                {
                    Plugin.Log.LogWarning("[Tutorial] trigger content " + _pending.name
                        + " never became visible — capture needed.");
                    _pending = null;
                    // The trigger-time claim must not outlive a box that never came.
                    if (_panel == null) SpeechService.EndModal();
                }
            }

            // A tracked modal that stopped showing ends cleanly: buffer cleared,
            // focus handed to whatever the modal hid.
            if (_panel != null && !PanelVisible())
            {
                EndModal();
                return;
            }

            // Dismissal verify: Enter fired CONTINUE; if the modal still shows a
            // beat later the click missed the game's dismissal machinery — that
            // state is invisible to the ear, so say it, and log it for capture.
            if (_verifyAt > 0f && Time.unscaledTime >= _verifyAt)
            {
                _verifyAt = -1f;
                if (PanelVisible())
                {
                    Plugin.Log.LogWarning("[Tutorial] CONTINUE click did not dismiss "
                        + _panel.name + " — dismissal path capture needed.");
                    SpeechService.Say(Lex.T("tutorial.still-open"), Priority.Queued, "tutorial");
                }
            }

            // CONTINUE focus while a modal is up: the game enforces this itself —
            // the panel Button's own FSM is a Set Selected watchdog (census; live
            // ride: re-asserts every ~8 frames against the response menu's
            // re-select). A mod-side yank joined that fight and its Select flushed
            // the buffer read mid-queue (ride capture 2026-07-26), so it is gone;
            // the ruling holds functionally through the input layer instead —
            // Enter targets CONTINUE explicitly and arrows reach the buffer via
            // Active(), wherever focus sits.

            // Fallback detection: a modal that took focus without a trigger signal.
            if (!Up()) return;
            var panel = PanelOf(Navigator.Current());
            if (panel != null) Adopt(panel);
        }

        private static void Adopt(Transform panel)
        {
            int id = panel.GetInstanceID();
            if (id == _panelId) return;
            _panelId = id;
            _panel = panel;

            Build(panel);
            Table.Reset();
            // The gate is normally already claimed at trigger time (the floor is
            // silent, chatter diverted); re-claiming is idempotent and covers the
            // focus-in fallback path. All blocks queue — nothing to stomp.
            SpeechService.BeginModal("tutorial");
            foreach (var block in _blocks)
                SpeechService.Say(block, Priority.Queued, "tutorial");
        }

        public static void Move(int delta) => Table.MoveRow(delta);
        public static void Repeat() => Table.MoveCol(0);

        /// <summary>Enter: always fire CONTINUE (owner ruling), wherever focus sits.
        /// Pointer-path dispatch — the f4641 capture showed the uGUI Button consuming
        /// submit while the game's dismissal machinery (FSM-fed, pointer-driven like
        /// a real mouse press) never saw it. The panel is re-checked a beat later;
        /// still showing means the path is wrong and gets spoken + logged.</summary>
        public static bool Dismiss()
        {
            var panel = _panel;
            if (panel == null && Up()) panel = PanelOf(Navigator.Current());
            if (panel == null) return false;
            var button = ContinueButton(panel);
            if (button == null)
            {
                Plugin.Log.LogWarning("[Tutorial] no Button component under " + panel.name);
                return false;
            }
            if (Time.unscaledTime - _lastDismiss < Timing.ActivateDebounce) return true;
            _lastDismiss = Time.unscaledTime;
            if (Navigator.Current() != button.gameObject)
                Navigator.Select(button.gameObject);
            Navigator.PointerClick(button.gameObject);
            _verifyAt = Time.unscaledTime + 0.6f;
            return true;
        }

        /// <summary>The modal stopped showing: clear the held buffer, hand focus
        /// to the surface beneath, then release the speech gate — held durable
        /// lines utter after the landing announce. Dialogue choices are the
        /// captured case (refocus a response so the menu is immediately walkable
        /// again); otherwise a stale in-modal selection is cleared so the next
        /// arrow adopts a live element.</summary>
        private static void EndModal()
        {
            Diag.Note("Tutorial", "modal ended — buffer cleared, refocus beneath");
            _panel = null;
            _panelId = 0;
            _verifyAt = -1f;
            _blocks.Clear();
            Table.Reset();

            var cur = Navigator.Current();
            if (cur != null && Util.HasAncestor(cur, "Tutorial System"))
            {
                var landed = false;
                if (DialogueState.MenuOpen)
                {
                    foreach (var b in Object.FindObjectsOfType<UnityEngine.UI.Button>())
                    {
                        if (b.gameObject.name.StartsWith("Response: ") && b.IsInteractable()
                            && b.gameObject.activeInHierarchy)
                        {
                            Navigator.Select(b.gameObject);
                            landed = true;
                            break;
                        }
                    }
                }
                if (!landed)
                {
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null) es.SetSelectedGameObject(null);
                }
            }
            SpeechService.EndModal();
        }

        /// <summary>The panel's CONTINUE: component-typed, not name-keyed.</summary>
        private static UnityEngine.UI.Button ContinueButton(Transform panel)
        {
            return panel != null ? panel.GetComponentInChildren<UnityEngine.UI.Button>(true) : null;
        }

        private static Transform PanelOf(GameObject go)
        {
            for (var cur = go.transform; cur != null; cur = cur.parent)
                if (cur.parent != null && cur.parent.name == "Tutorial System")
                    return cur;
            return null;
        }

        // ---------- Universal harvest ----------

        /// <summary>Every visible rendered text in the panel subtree, sorted into
        /// screen reading order (top-to-bottom, left-to-right — ride capture: the
        /// box title rendered last in hierarchy order and spoke at the end),
        /// paragraph-split into walkable blocks; prompt gaps filled, sprite tags
        /// transcoded, CONTINUE last. No child names anywhere.</summary>
        private static void Build(Transform panel)
        {
            _blocks.Clear();
            var button = ContinueButton(panel);

            var texts = new List<(Vector3 pos, TMP_Text tmp)>();
            foreach (var tmp in panel.GetComponentsInChildren<TMP_Text>(false))
            {
                if (button != null && tmp.transform.IsChildOf(button.transform)) continue;
                if (InsidePrompt(tmp.transform, panel)) continue;
                if (Util.AlphaUpTo(tmp.transform, panel.parent) < 0.05f) continue;
                if (string.IsNullOrEmpty(tmp.text) || tmp.text.Trim().Length <= 1) continue;
                texts.Add((tmp.transform.position, tmp));
            }
            texts.Sort((a, b) => a.pos.y != b.pos.y
                ? b.pos.y.CompareTo(a.pos.y)
                : a.pos.x.CompareTo(b.pos.x));

            // Slash-decorated blocks ("//SKILL CHECKS", "//CONTROLS") are the
            // game's own label markers — annotations anchored to the screen areas
            // they describe, so position-sorting puts them wherever they point
            // (ride capture: SKILL CHECKS anchored by the response menu, spoke
            // last). Spoken form: they follow the lead block as the box's topics.
            var body = new List<string>();
            var labels = new List<string>();
            foreach (var (_, tmp) in texts)
            {
                string filled = FillPromptGaps(tmp, tmp.text);
                foreach (var para in Regex.Split(filled, @"\n\s*\n"))
                {
                    bool isLabel = para.TrimStart().StartsWith("/");
                    string block = CleanBlock(para);
                    if (string.IsNullOrEmpty(block) || block.Length <= 1) continue;
                    var list = isLabel ? labels : body;
                    if (!list.Contains(block)) list.Add(block);
                }
            }
            if (body.Count > 0) _blocks.Add(body[0]);
            _blocks.AddRange(labels);
            for (int i = 1; i < body.Count; i++)
                if (!_blocks.Contains(body[i])) _blocks.Add(body[i]);

            if (button != null)
            {
                var label = button.GetComponentInChildren<TMP_Text>(false);
                string text = label != null ? SpeechService.Clean(label.text) : null;
                if (!string.IsNullOrEmpty(text))
                    _blocks.Add(text + Lex.T("tutorial.button-suffix"));
            }

            if (_blocks.Count == 0)
            {
                Plugin.Log.LogWarning("[Tutorial] harvest found no text under "
                    + panel.name + " — capture needed.");
                _blocks.Add(Lex.T("tutorial.empty"));
            }
        }

        private static bool InsidePrompt(Transform t, Transform root)
        {
            for (var cur = t; cur != null && cur != root; cur = cur.parent)
                if (cur.name.IndexOf("prompt", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // ---------- Prompt transcode (B2: the mod's keys) ----------

        /// <summary>Structural prompt-name -> lexicon key for controls the mod binds.
        /// Contains-matched against prompt object names; extend as prompts are
        /// captured. Unlisted prompts fall through to their own rendered text.</summary>
        private static readonly (string fragment, string lexKey)[] PromptKeys =
        {
            ("A Prompt", "prompt.enter"),
            ("B Prompt", "prompt.backspace"),
            ("DPAD", "prompt.arrows"),
        };

        /// <summary>Gamepad vocabulary the game authors as words inside text (CS1
        /// specimens: "the A button and the D-pad"); replaced with the mod's keys at
        /// announce time. Seeded with the mod's current bindings only; grows per
        /// capture.</summary>
        private static readonly (string phrase, string lexKey)[] AuthoredPhrases =
        {
            ("the A button", "prompt.enter"),
            ("the B button", "prompt.backspace"),
            ("the D-pad", "prompt.arrows"),
        };

        /// <summary>Fill a text's whitespace gaps ("Select locations with [gap] and
        /// [gap].") from its active prompt-child objects, sorted into reading order
        /// by screen position (top-to-bottom, left-to-right). Word resolution: mod
        /// key map -> the prompt's own rendered text -> loud placeholder. Count
        /// mismatch appends instead of guessing, logged.</summary>
        private static string FillPromptGaps(TMP_Text tmp, string txt)
        {
            var prompts = new List<(Vector3 pos, string word)>();
            foreach (Transform child in tmp.transform)
            {
                if (!child.gameObject.activeSelf) continue;
                string n = child.name;
                if (n.IndexOf("prompt", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Target", System.StringComparison.Ordinal) >= 0) continue;
                if (n.IndexOf("postioner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                prompts.Add((child.position, PromptWord(child)));
            }
            if (prompts.Count == 0) return txt;
            prompts.Sort((a, b) => a.pos.y != b.pos.y
                ? b.pos.y.CompareTo(a.pos.y)
                : a.pos.x.CompareTo(b.pos.x));

            var segments = Regex.Split(txt, @"[ \t]{3,}");
            if (segments.Length == prompts.Count + 1)
            {
                var sb = new System.Text.StringBuilder(segments[0]);
                for (int i = 0; i < prompts.Count; i++)
                    sb.Append(' ').Append(prompts[i].word).Append(' ').Append(segments[i + 1].TrimStart());
                return sb.ToString();
            }
            Plugin.Log.LogInfo("[Tutorial] prompt gap mismatch under " + tmp.name + ": "
                + prompts.Count + " prompt(s), " + (segments.Length - 1) + " gap(s) — appending.");
            var words = new List<string>();
            foreach (var p in prompts) words.Add(p.word);
            return txt + ". " + Lex.T("tutorial.shown-with") + " "
                + string.Join(Lex.T("tutorial.and"), words) + ".";
        }

        private static string PromptWord(Transform prompt)
        {
            foreach (var (fragment, lexKey) in PromptKeys)
                if (prompt.name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return Lex.T(lexKey);
            var tmp = prompt.GetComponentInChildren<TMP_Text>(false);
            string rendered = tmp != null ? SpeechService.Clean(tmp.text) : null;
            if (!string.IsNullOrEmpty(rendered)) return rendered;
            Plugin.Log.LogWarning("[Tutorial] UNMAPPED PROMPT: " + prompt.name);
            return Lex.T("prompt.unknown");
        }

        // ---------- Text transcode seams ----------

        /// <summary>Sprite-glyph -> spoken word for in-text sprite tags. Still empty
        /// — CS2's first prompt-bearing tutorial used sibling prompt objects, not
        /// tags — but the seam stays armed: any tag speaks a placeholder and logs
        /// loudly. Transcode-not-strip discipline.</summary>
        private static readonly Dictionary<string, string> GlyphWords =
            new Dictionary<string, string>();

        private static readonly Regex SpriteTag =
            new Regex("<sprite[^>]*?(?:name=\"([^\"]+)\")?[^>]*>", RegexOptions.Compiled);

        private static string CleanBlock(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            // The game decorates box/area labels with a leading slash run
            // ("//SKILL CHECKS", "//CONTROLS") — visual styling, not content
            // (same transcode Describe applies to "//INTERFACE" skill tags).
            raw = raw.TrimStart();
            if (raw.StartsWith("/")) raw = raw.TrimStart('/', ' ');
            string transcoded = SpriteTag.Replace(raw, m =>
            {
                string name = m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                if (GlyphWords.TryGetValue(name, out var word)) return " " + word + " ";
                Plugin.Log.LogWarning("[Tutorial] UNMAPPED GLYPH: " + m.Value);
                return " " + Lex.T("prompt.unknown") + " ";
            });
            foreach (var (phrase, lexKey) in AuthoredPhrases)
            {
                if (transcoded.IndexOf(phrase, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                transcoded = Regex.Replace(transcoded, Regex.Escape(phrase), Lex.T(lexKey),
                    RegexOptions.IgnoreCase);
            }
            return SpeechService.Clean(transcoded);
        }
    }
}
