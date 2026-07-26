using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sleeptalker.Scaffold
{
    /// <summary>
    /// Diagnostic hub: frame-stamped CAUSE recording (key presses, synthetic nav
    /// events) in an always-on in-memory ring, plus the F3 incident dump — one
    /// contiguous log block of recent causes, recent FSM state entries, recent
    /// speech, and the mod's believed state at the moment something sounded wrong.
    /// Live [Input]/[Nav] log lines are gated by Debug.TraceInput (default off) so
    /// the log never floods; the ring records regardless, and the dump is where
    /// the evidence surfaces.
    ///
    /// Snapshot() is also read by the bridge's /modstate endpoint via reflection —
    /// keep its return shape JSON-plain (Dictionary/List/primitives only) and its
    /// signature stable.
    /// </summary>
    internal static class Diag
    {
        private struct Event
        {
            public int Frame;
            public float T;
            public string Kind;
            public string Detail;
        }

        private const int RingSize = 96;
        private static readonly Event[] Ring = new Event[RingSize];
        private static long _count;

        // ---------- Tier seams (stage 1, 2026-07-25) ----------
        // Diag is scaffold: it owns the rings, stamps, and dump machinery, but the
        // mod's believed state lives in the middleware and game tiers. Plugin
        // registers these providers at init; every read is guarded so a broken
        // provider never costs the rest of the evidence. Snapshot()'s shape and
        // key names are unchanged (the bridge's /modstate reflects them).

        /// <summary>Spoken-name of the current mode (Plugin: ModeModel).</summary>
        public static Func<string> ModeNameProvider;

        /// <summary>Believed-state entries beyond the scaffold's own (window flags,
        /// conversation, response menu, input pause). Keys are the /modstate
        /// contract; the provider guards each entry individually.</summary>
        public static Func<Dictionary<string, object>> StateProvider;

        /// <summary>Recent FSM state entries, newest first (Plugin: FsmSignals).</summary>
        public static Func<int, List<(int Frame, string Owner, string State)>> FsmRecentProvider;

        // ---------- Mode cache (one derivation per frame, shared by all stamps) ----------

        private static int _modeFrame = -1;
        private static string _modeName = "?";

        /// <summary>Spoken-name of the current mode, computed at most once per frame.</summary>
        public static string ModeNow()
        {
            if (Time.frameCount != _modeFrame)
            {
                _modeFrame = Time.frameCount;
                try { _modeName = ModeNameProvider != null ? ModeNameProvider() : "?"; }
                catch { _modeName = "?"; }
            }
            return _modeName;
        }

        /// <summary>Shared-clock stamp: frame number joins mod log lines to bridge
        /// /watch entries (both main-thread), time to wall-clock reports.</summary>
        public static string Stamp()
            => "f" + Time.frameCount + " t" + Time.realtimeSinceStartup.ToString("F2");

        // ---------- Cause recording ----------

        /// <summary>Record a cause. Always ringed (with the mode the mod believed);
        /// logged live only when Debug.TraceInput is on.</summary>
        public static void Note(string kind, string detail)
        {
            string stored = ModeNow() + " | " + detail;
            Ring[_count % RingSize] = new Event
            {
                Frame = Time.frameCount,
                T = Time.realtimeSinceStartup,
                Kind = kind,
                Detail = stored,
            };
            _count++;
            if (Plugin.TraceInput != null && Plugin.TraceInput.Value)
                Plugin.Log.LogInfo("[" + kind + "] " + Stamp() + " " + stored);
        }

        private static readonly KeyCode[] TrackedKeys =
        {
            KeyCode.F1, KeyCode.F3, KeyCode.Z, KeyCode.C, KeyCode.V, KeyCode.K, KeyCode.L,
            KeyCode.R, KeyCode.T, KeyCode.I, KeyCode.U, KeyCode.J, KeyCode.O,
            KeyCode.Space, KeyCode.Slash, KeyCode.Backspace, KeyCode.Escape,
            KeyCode.Return, KeyCode.KeypadEnter,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        };

        /// <summary>Ring every mod-relevant key the moment it goes down, before any
        /// dispatch decision — so a swallowed or misrouted key still leaves a trace.</summary>
        public static void CaptureKeys(bool shift)
        {
            if (!Input.anyKeyDown) return;
            foreach (var key in TrackedKeys)
                if (Input.GetKeyDown(key))
                    Note("Input", (shift ? "Shift+" : "") + key);
        }

        private static List<Event> RecentEvents(int max)
        {
            var result = new List<Event>(max);
            long start = Math.Max(0, _count - max);
            for (long i = start; i < _count; i++)
                result.Add(Ring[i % RingSize]);
            return result;
        }

        // ---------- Believed state (shared by dump and /modstate) ----------

        private static Dictionary<string, object> StateNow()
        {
            var state = new Dictionary<string, object>
            {
                ["frame"] = Time.frameCount,
                ["time"] = Time.realtimeSinceStartup,
                ["mode"] = ModeNow(),
            };
            try { state["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            try
            {
                if (StateProvider != null)
                    foreach (var kv in StateProvider())
                        state[kv.Key] = kv.Value;
            }
            catch { }
            try
            {
                var selected = Navigator.Current();
                state["selection"] = selected != null ? Util.PathOf(selected) : null;
            }
            catch { }
            try { state["speechQueueDepth"] = SpeechService.QueueDepth; } catch { }
            return state;
        }

        // ---------- F3 incident dump ----------

        /// <summary>One contiguous log block: believed state, recent causes, recent
        /// FSM state entries, recent speech. Every section is independently guarded —
        /// a broken reader must not cost the rest of the evidence.</summary>
        public static void IncidentDump(string reason)
        {
            var sb = new StringBuilder(4096);
            sb.Append("[Diag] ===== INCIDENT (").Append(reason).Append(") ").Append(Stamp())
              .AppendLine(" =====");

            try
            {
                foreach (var kv in StateNow())
                    sb.Append("[Diag] state ").Append(kv.Key).Append('=')
                      .Append(kv.Value ?? "(null)").AppendLine();
            }
            catch (Exception e) { sb.AppendLine("[Diag] state read failed: " + e.Message); }

            sb.AppendLine("[Diag] -- recent input/nav (oldest first) --");
            try
            {
                foreach (var e in RecentEvents(24))
                    sb.Append("[Diag] f").Append(e.Frame).Append(" t").Append(e.T.ToString("F2"))
                      .Append(' ').Append(e.Kind).Append(' ').AppendLine(e.Detail);
            }
            catch (Exception e) { sb.AppendLine("[Diag] input ring read failed: " + e.Message); }

            sb.AppendLine("[Diag] -- recent FSM state entries (newest first) --");
            try
            {
                if (FsmRecentProvider != null)
                    foreach (var r in FsmRecentProvider(24))
                        sb.Append("[Diag] f").Append(r.Frame).Append(' ')
                          .Append(r.Owner).Append(" -> ").AppendLine(r.State);
            }
            catch (Exception e) { sb.AppendLine("[Diag] fsm ring read failed: " + e.Message); }

            sb.AppendLine("[Diag] -- recent speech (oldest first) --");
            try
            {
                foreach (var line in SpeechService.RecentHistory(10))
                    sb.Append("[Diag] ").AppendLine(line);
            }
            catch (Exception e) { sb.AppendLine("[Diag] speech history read failed: " + e.Message); }

            sb.Append("[Diag] ===== END INCIDENT ").Append(Stamp()).Append(" =====");
            Plugin.Log.LogInfo(sb.ToString());
        }

        // ---------- Bridge snapshot (/modstate reads this via reflection) ----------

        public static Dictionary<string, object> Snapshot()
        {
            var result = new Dictionary<string, object> { ["state"] = StateNow() };

            try
            {
                var input = new List<object>();
                foreach (var e in RecentEvents(20))
                    input.Add(new Dictionary<string, object>
                    {
                        ["f"] = e.Frame,
                        ["t"] = e.T,
                        ["kind"] = e.Kind,
                        ["detail"] = e.Detail,
                    });
                result["inputRecent"] = input;
            }
            catch (Exception e) { result["inputRecent"] = "error: " + e.Message; }

            try
            {
                var fsm = new List<object>();
                if (FsmRecentProvider != null)
                    foreach (var r in FsmRecentProvider(24))
                        fsm.Add(new Dictionary<string, object>
                        {
                            ["f"] = r.Frame,
                            ["owner"] = r.Owner,
                            ["state"] = r.State,
                        });
                result["fsmRecent"] = fsm;
            }
            catch (Exception e) { result["fsmRecent"] = "error: " + e.Message; }

            try { result["speechRecent"] = SpeechService.RecentHistory(10); }
            catch (Exception e) { result["speechRecent"] = "error: " + e.Message; }

            return result;
        }
    }
}
