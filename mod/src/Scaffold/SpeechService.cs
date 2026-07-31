using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Sleeptalker.Scaffold
{
    internal enum Priority
    {
        /// <summary>Interrupts current speech immediately (user-initiated navigation/queries).</summary>
        Immediate,
        /// <summary>Appended to the announcement queue (dialogue, notifications, tutorials).</summary>
        Queued,
    }

    /// <summary>Central speech output: Tolk bridge, announcement queue, history, text cleaning.
    /// All calls must come from the main thread.</summary>
    internal static class SpeechService
    {
        private const int HistoryCapacity = 200;

        private struct Pending
        {
            public string Text;
            public string Source;
        }

        private static bool _loaded;
        private static bool _available;
        private static readonly Queue<Pending> Queue = new Queue<Pending>();
        private static readonly List<string> History = new List<string>();
        private static string _lastQueued;
        private static float _lastSpokeAt;

        private static readonly Regex TagPattern = new Regex("<[^>]{1,64}?>", RegexOptions.Compiled);
        private static readonly Regex SpacePattern = new Regex("[ \t]{2,}", RegexOptions.Compiled);
        /// <summary>B2/C6: punctuation stutters — sprite-boundary seams ("action,.
        /// select") and authored double periods ("actions..") — collapse to the run's
        /// first mark. Spaces between marks count as part of the run (", ." shapes).</summary>
        private static readonly Regex StutterPattern =
            new Regex("([.,])(?:\\s*[.,])+", RegexOptions.Compiled);

        public static void Init()
        {
            try
            {
                Tolk.Tolk_TrySAPI(true);
                Tolk.Tolk_PreferSAPI(false);
                Tolk.Tolk_Load();
                _loaded = Tolk.Tolk_IsLoaded();
                _available = _loaded && Tolk.Tolk_HasSpeech();
                string reader = _loaded ? Tolk.DetectScreenReader() : null;
                Plugin.Log.LogInfo($"[Speech:init] Tolk loaded={_loaded} speech={_available} reader={reader ?? "(sapi/none)"}");
            }
            catch (DllNotFoundException)
            {
                Plugin.Log.LogError("[Speech:init] Tolk.dll not found beside the game executable — speech disabled, logging only.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[Speech:init] " + e.Message);
            }
        }

        public static void Shutdown()
        {
            if (_loaded)
            {
                try { Tolk.Tolk_Unload(); } catch { }
            }
        }

        /// <summary>Pump the queue: speak the next queued announcement once the reader is free.</summary>
        public static void Tick()
        {
            if (Queue.Count == 0) return;
            if (Time.unscaledTime - _lastSpokeAt < Timing.SpeechQueueGap) return;
            try
            {
                if (_loaded && Tolk.Tolk_IsSpeaking()) return;
            }
            catch { }
            var next = Queue.Dequeue();
            Emit(next.Text, interrupt: false, next.Source);
        }

        public static void Say(string text, Priority priority, string source)
        {
            text = Clean(text);
            if (string.IsNullOrEmpty(text)) return;

            // Modal primacy (owner design 2026-07-26): while a modal surface holds
            // the audio, only its own source speaks live. Volatile sources (focus,
            // nav, odds — lines that regenerate fresh on dismissal) are dropped;
            // everything else is held latest-per-source and uttered after release.
            // Durable is the DEFAULT so an unregistered future watcher can never be
            // silently lost — the worst default failure is a redundant line.
            if (_modalSource != null && source != _modalSource)
            {
                if (!VolatileSources.Contains(source))
                    PenKeepLatest(new Pending { Text = text, Source = source });
                return;
            }

            if (priority == Priority.Immediate)
            {
                // Interrupt current speech but preserve queued lines — queue flushing is
                // an explicit act (Navigator.Select, Stop), not a side effect of speaking.
                Emit(text, interrupt: true, source);
            }
            else
            {
                if (text == _lastQueued) return;
                foreach (var p in Queue)
                    if (p.Text == text) return;
                if (Queue.Count >= 20)
                {
                    Plugin.Log.LogWarning($"[Speech:{source}] queue full, dropping: {text}");
                    return;
                }
                _lastQueued = text;
                Queue.Enqueue(new Pending { Text = text, Source = source });
                Plugin.Log.LogInfo($"[Speech:{source}] [{Diag.ModeNow()} {Diag.Stamp()}] (queued) {text}");
            }
        }

        /// <summary>Pending queued announcements (Diag state reads).</summary>
        public static int QueueDepth => Queue.Count;

        /// <summary>Tail of the spoken history, oldest first (Diag incident dump / /modstate).</summary>
        public static List<string> RecentHistory(int max)
        {
            int start = Mathf.Max(0, History.Count - max);
            return History.GetRange(start, History.Count - start);
        }

        /// <summary>Drop pending queued announcements (used when the user navigates away).</summary>
        public static void FlushQueue()
        {
            Queue.Clear();
            _lastQueued = null;
        }

        // ---------- Modal primacy gate (owner design 2026-07-26) ----------

        private static string _modalSource;
        private static readonly HashSet<string> VolatileSources = new HashSet<string>();
        private static readonly List<Pending> Pen = new List<Pending>();

        /// <summary>Mark a source's lines as regenerating (focus, nav, odds): under
        /// a modal they drop instead of holding, because dismissal re-fires them
        /// fresh and a held copy would duplicate or contradict the fresh read.
        /// Registration happens in the composition root — this tier stays
        /// game-agnostic.</summary>
        public static void RegisterVolatile(string source) => VolatileSources.Add(source);

        /// <summary>A modal surface claims the audio. Pending queued lines from
        /// durable sources are preserved into the holding pen (latest per source);
        /// the rest of the queue — the chatter fighting the modal's arrival — is
        /// killed. Chained modals keep the pen: it flushes only on release.</summary>
        public static void BeginModal(string source)
        {
            _modalSource = source;
            foreach (var p in Queue)
                if (p.Source != source && !VolatileSources.Contains(p.Source))
                    PenKeepLatest(p);
            Queue.Clear();
            _lastQueued = null;
            // Claiming primacy takes the floor: whatever is mid-utterance stops
            // now, cleanly, instead of being stomped mid-word when the modal's
            // first line arrives (ride capture 2026-07-26).
            try { if (_loaded) Tolk.Tolk_Silence(); } catch { }
        }

        /// <summary>Release the modal claim and utter the held lines, in arrival
        /// order, through the normal queued path (its dedupe applies).</summary>
        public static void EndModal()
        {
            if (_modalSource == null) return;
            _modalSource = null;
            if (Pen.Count == 0) return;
            var held = new List<Pending>(Pen);
            Pen.Clear();
            foreach (var p in held)
                Say(p.Text, Priority.Queued, p.Source);
        }

        private static void PenKeepLatest(Pending p)
        {
            for (int i = 0; i < Pen.Count; i++)
                if (Pen[i].Source == p.Source) { Pen.RemoveAt(i); break; }
            Pen.Add(p);
        }

        /// <summary>Drop only queued entries from one source (e.g. stale game-driven focus
        /// chatter when the user starts navigating), preserving dialogue and outcomes.</summary>
        public static void FlushSource(string source)
        {
            if (Queue.Count == 0) return;
            var keep = new List<Pending>();
            foreach (var p in Queue)
                if (p.Source != source) keep.Add(p);
            Queue.Clear();
            foreach (var p in keep) Queue.Enqueue(p);
        }

        public static void Stop()
        {
            FlushQueue();
            try { if (_loaded) Tolk.Tolk_Silence(); } catch { }
        }

        private static void Emit(string text, bool interrupt, string source)
        {
            _lastSpokeAt = Time.unscaledTime;
            History.Add(text);
            if (History.Count > HistoryCapacity)
                History.RemoveRange(0, History.Count - HistoryCapacity);

            // The [mode fN tN] context block joins each utterance to the input ring,
            // FSM signals, and the bridge's /watch stream (shared frame clock).
            Plugin.Log.LogInfo($"[Speech:{source}] [{Diag.ModeNow()} {Diag.Stamp()}] {text}");
            try
            {
                if (_loaded) Tolk.Tolk_Output(text, interrupt);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Speech:err] " + e.Message);
            }
        }

        public static void RepeatLast()
        {
            if (History.Count == 0) { Say("No speech history.", Priority.Immediate, "history"); return; }
            SpeakRaw(History[History.Count - 1]);
        }

        private static void SpeakRaw(string text)
        {
            try { if (_loaded) Tolk.Tolk_Output(text, true); } catch { }
            Plugin.Log.LogInfo("[Speech:history] " + text);
        }

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.Replace('�', '\'');
            text = TagPattern.Replace(text, " ");
            text = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            text = StutterPattern.Replace(text, "$1");
            text = SpacePattern.Replace(text, " ").Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
