using System;
using System.Collections.Generic;
using UnityEngine;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Player vitals — the groundwork registry for the eventual vitals
    /// readout surface (owner direction 2026-07-26).
    ///
    /// The CS2 HUD renders each vital as a bar owned by an FSM that polls the value
    /// and enters an update state only when the bar actually changes (stress:
    /// "Get Stress" -> "Update Bar" event -> "Animation and Sound", census-verified).
    /// That state entry is therefore a render-honest change clock, and the bar FSM's
    /// own variables are the value the bar draws. Each channel names that dial:
    /// owner prefix, update state, value/capacity readers. Announce-on-change rides
    /// the clock; the future readout hotkey iterates the same channels via Read().
    ///
    /// Capacity comes from the game's own variant naming — the active stress bar is
    /// "Stress System {6|8|10} {Safe|Risky|Danger}", difficulty-selected; only the
    /// active variant's FSM runs, so signals self-select the right bar.
    ///
    /// First signal after load caches silently (the bar animating up to the loaded
    /// value is boot noise, same policy as the title-flow boot sweep); every later
    /// change announces. Crew stress bars share the update-state name but not the
    /// owner prefix — they stay out of this registry until crew surfaces are built.</summary>
    internal static class Vitals
    {
        internal sealed class Channel
        {
            public string Name;                       // spoken name ("Stress")
            public string OwnerPrefix;                // HUD bar owner GameObject name prefix
            public string UpdateState;                // the bar's change-clock state
            public Func<PlayMakerFSM, float> Value;   // current value, from the bar's own vars
            public Func<PlayMakerFSM, float> Max;     // capacity; 0 = unknown, spoken without "of"
            public float? LastSpoken;                 // mod-side cache: direction + first-signal mute
            public PlayMakerFSM LiveFsm;              // last bar seen alive, for on-demand Read()
        }

        private static readonly List<Channel> Channels = new List<Channel>();

        public static void Init()
        {
            Register(new Channel
            {
                Name = Lex.T("vitals.stress"),
                OwnerPrefix = "Stress System ",
                UpdateState = "Animation and Sound",
                Value = fsm => FloatVar(fsm, "Stress"),
                Max = fsm => FirstNumberIn(fsm.gameObject.name),
            });
        }

        private static void Register(Channel ch)
        {
            Channels.Add(ch);
            FsmSignals.Subscribe(null, ch.UpdateState, (fsm, state) => OnChanged(ch, fsm));
        }

        private static void OnChanged(Channel ch, PlayMakerFSM fsm)
        {
            if (fsm == null || !fsm.gameObject.name.StartsWith(ch.OwnerPrefix)) return;
            ch.LiveFsm = fsm;

            float value = ch.Value(fsm);
            if (ch.LastSpoken == null)
            {
                ch.LastSpoken = value;   // load-time catch-up: cache, stay silent
                return;
            }
            if (value == ch.LastSpoken.Value) return;

            string direction = Lex.T(value > ch.LastSpoken.Value ? "vitals.up" : "vitals.down");
            ch.LastSpoken = value;
            SpeechService.Say(ch.Name + " " + direction + ", " + Format(value, ch.Max(fsm)) + ".",
                Priority.Queued, "vitals");
        }

        /// <summary>Current readout of every channel with a live bar — the vitals
        /// hotkey, when it exists, speaks exactly this list.</summary>
        public static List<string> Read()
        {
            var lines = new List<string>();
            foreach (var ch in Channels)
            {
                var fsm = ch.LiveFsm;
                if (fsm == null || !fsm.gameObject.activeInHierarchy) continue;
                lines.Add(ch.Name + " " + Format(ch.Value(fsm), ch.Max(fsm)) + ".");
            }
            return lines;
        }

        private static string Format(float value, float max)
        {
            string v = value.ToString("0.#");
            return max > 0f ? v + " " + Lex.T("vitals.of") + " " + max.ToString("0.#") : v;
        }

        private static float FloatVar(PlayMakerFSM fsm, string name)
        {
            var f = fsm.FsmVariables.GetFsmFloat(name);
            return f != null ? f.Value : 0f;
        }

        private static float FirstNumberIn(string name)
        {
            int start = -1;
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsDigit(name[i])) { if (start < 0) start = i; }
                else if (start >= 0) return float.Parse(name.Substring(start, i - start));
            }
            return start >= 0 ? float.Parse(name.Substring(start)) : 0f;
        }
    }
}
