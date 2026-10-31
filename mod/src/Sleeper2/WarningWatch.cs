using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Warning boxes (2026-10-25; the Citizen Speaker watcher, ported):
    /// the title's slot-overwrite warning ("THIS WILL ERASE THE SAVE DATA IN THIS
    /// SLOT. CONTINUE?") and the pause menu's quit warnings ("QUIT TO MENU?
    /// PROGRESS SINCE LAST AUTOSAVE WILL BE LOST.") spoke only their buttons, and
    /// the permadeath end screen nothing at all. Each
    /// box's own rendered text is spoken once when it opens. The boxes are held
    /// through their always-active root, which reaches them while hidden.</summary>
    internal static class WarningWatch
    {
        private sealed class Box
        {
            public string Root;      // an always-active root GameObject
            public string Path;      // the box under it
            public Transform Held;
            public bool WasOpen;
        }

        private static readonly Box[] Boxes =
        {
            new Box { Root = "MAIN MENU", Path = "Demo Menu/Warning Menu" },
            new Box { Root = "PAUSE", Path = "Pause Canvas/Warning Menu Quit Menu" },
            new Box { Root = "PAUSE", Path = "Pause Canvas/Warning Menu Quit Game" },
            // The DANGEROUS run's end screen ("THE BELT FADES... Your DANGEROUS run
            // has come to an end..." + QUIT): the Death Machine's PERMADEAD branch.
            new Box { Root = "Letterbox Canvas", Path = "End Dead" },
        };

        private const float FindEvery = 1f;
        private static float _nextFind;

        public static void OnSceneChanged()
        {
            foreach (var b in Boxes) { b.Held = null; b.WasOpen = false; }
            _nextFind = 0f;
        }

        public static void Tick()
        {
            bool findNow = Time.unscaledTime >= _nextFind;
            if (findNow) _nextFind = Time.unscaledTime + FindEvery;
            foreach (var b in Boxes)
            {
                if (b.Held == null)
                {
                    if (!findNow) continue;
                    var root = GameObject.Find(b.Root);
                    b.Held = root != null ? root.transform.Find(b.Path) : null;
                    if (b.Held == null) continue;
                    b.WasOpen = Open(b.Held); // already open when found: no announce
                    continue;
                }
                bool open = Open(b.Held);
                if (open && !b.WasOpen)
                {
                    string text = BoxText(b.Held);
                    if (text != null)
                    {
                        Plugin.Log.LogInfo("[Warning] " + b.Path + ": " + text);
                        // The answer to the player's Enter on the button that raised it.
                        SpeechService.Say(text, Priority.Immediate, "warning");
                    }
                    else { open = false; } // drawn before its text: next frame
                }
                b.WasOpen = open;
            }
        }

        private static bool Open(Transform t) => t.gameObject.activeInHierarchy && Util.RenderedUp(t);

        private static string BoxText(Transform box)
        {
            var texts = new List<TMPro.TMP_Text>();
            foreach (var tmp in box.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (!Util.RenderedUp(tmp.transform)) continue;
                if (string.IsNullOrEmpty(SpeechService.Clean(tmp.text))) continue;
                texts.Add(tmp);
            }
            if (texts.Count == 0) return null;
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
