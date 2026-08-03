using System.Collections.Generic;
using UnityEngine;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The station census — the CS1 port (P2; CS1 StationCensus.cs is
    /// the design of record, ported 2026-08-03 on owner direction): spoken
    /// appearance/disappearance callouts for location markers — the sighted
    /// player's "a new marker faded in", anywhere the zone atlas serves (hub,
    /// rig, contract floors). LOCATIONS ONLY: the CS1 character construction
    /// (Character Button / Click to Play canvases) does not exist in CS2 —
    /// verified against the census corpus before build (zero hits), owner
    /// request of record.
    ///
    /// Identity is the zone atlas key set (marker dial states, camera-
    /// independent — the CS1 trap: frustum churn must never read as change).
    /// Two channels, owner-ruled (CS1):
    /// - Cause-driven notifications, PRESENT tense, attached to the TAIL of
    ///   the causing beat (outcome read, cycle summary, conversation end)
    ///   via OnBeat — behind the beat's own queued speech, never over it,
    ///   never on dead air, never on a keypress.
    /// - N replays the last recorded change, PAST tense, unconditionally.
    ///   No freshness state: tense IS the freshness marker.</summary>
    internal static class StationCensus
    {
        private struct Change
        {
            public string Key;
            public string Name;
            public bool Appeared;
        }

        // Identity is PER SURFACE (owner-confirmed live 2026-08-03: the G
        // toggle swaps which container the atlas serves — hub locations vs
        // rig rooms — and a single known-set diffed the swap as ±6–9 phantom
        // "changes"). A surface swap switches WHICH set we diff against;
        // presence follows the markers' own dials, never container
        // visibility. Real changes on a surface you left speak when you
        // return to it (its own set diffs then).
        private static readonly Dictionary<string, Dictionary<string, string>>
            KnownBySurface = new Dictionary<string, Dictionary<string, string>>();
        private static Dictionary<string, string> _candidate;
        private static string _candidateSurface;
        private static readonly List<Change> Pending = new List<Change>();
        private static readonly List<Change> Last = new List<Change>();

        private static float _diffDueAt = -1f;
        private static float _beatWindowUntil = -1f;
        private static float _flushNotBefore;
        private static float _pendingSince = -1f;
        private static bool _staleLogged;
        private static float _nextBaselineTry;

        public static void Init()
        {
            // Population signals: the marker dials' own boundary states — the
            // atlas Listed set is the arrival side, Off/Off 2 the removal
            // side. Filter = the marker anatomy (a Location Contents child),
            // never instance names (universal-hooks law). The game's own
            // new-location flash lives on the same dial family ("Clock
            // Flasher"/"Flashed Already?"), so the arrival sound moment IS a
            // subscribed signal.
            // CRISIS!/CRISIS OVER! are the dial's one-shot crisis add/remove
            // beats (D-decode 2026-08-03: never resting states — the appear
            // path settles in Variables Met, the remove path in Off 2), so
            // subscribing them lands the diff at the crisis moment itself.
            foreach (var state in new[]
                { "Variables Met", "Off Camera", "Selected", "Cycle Check",
                  "Clock Flasher", "Flashed Already?", "Off", "Off 2",
                  "Off + Destroy", "CRISIS!", "CRISIS OVER!" })
                FsmSignals.Subscribe(null, state, OnMarkerSignal);
            // A conversation's tail is a beat (story flags land at end).
            ConversationEvents.ConversationEnded += OnBeat;
        }

        private static void OnMarkerSignal(PlayMakerFSM fsm, string state)
        {
            if (fsm == null || fsm.gameObject == null) return;
            if (fsm.transform.Find("Location Contents") == null) return;
            // Coalesce bursts (a zone settle flips several markers): the diff
            // runs once, a beat after the churn quiets.
            _diffDueAt = Time.unscaledTime + Timing.CensusCoalesceDefer;
        }

        /// <summary>Scene load: re-baseline silently — cross-load diffs are
        /// unknowable (CS1 law).</summary>
        public static void OnSceneChanged()
        {
            KnownBySurface.Clear();
            _candidate = null;
            _candidateSurface = null;
            Pending.Clear();
            Last.Clear();
            _diffDueAt = -1f;
            _beatWindowUntil = -1f;
            _pendingSince = -1f;
        }

        /// <summary>A story beat's tail: outcome read composed, cycle summary
        /// spoken, conversation ended. Diff now and open the flush window —
        /// the window absorbs the dials' own poll latency (the beat writes
        /// the flag; the marker flips a beat later).</summary>
        public static void OnBeat()
        {
            _beatWindowUntil = Time.unscaledTime + Timing.CensusBeatWindow;
            _flushNotBefore = Time.unscaledTime + Timing.CensusFlushLeadIn;
            Diff();
        }

        /// <summary>The active surface — same dial Build() filters by, so a
        /// snapshot and its surface key can never disagree within a frame.</summary>
        private static string Surface()
            => GameQueries.RigSide() ? "rig" : "hub";

        public static void Tick()
        {
            if (!KnownBySurface.ContainsKey(Surface())) { TryBaseline(); return; }
            // Signal-due diffs wait for the world AT REST (CS1 ride law:
            // conversations and cycle turnover churn the dials — mid-flight
            // reads are not story truth). The due-stamp holds until rest.
            if (_diffDueAt > 0f && Time.unscaledTime >= _diffDueAt && WorldAtRest())
            {
                _diffDueAt = -1f;
                Diff();
            }
            if (Pending.Count > 0 && Time.unscaledTime < _beatWindowUntil
                && Time.unscaledTime >= _flushNotBefore)
                TryFlush();
            // Pause rescue (CS1 A6): unflushed changes would be lost to the
            // next baseline — the pause menu is modally quiet and at rest.
            if (Pending.Count > 0 && ModeModel.Current() == Mode.Pause)
                EmitPending();
            if (Pending.Count > 0 && _pendingSince > 0f && !_staleLogged
                && Time.unscaledTime - _pendingSince > Timing.CensusStaleLogAfter)
            {
                _staleLogged = true;
                Plugin.Log.LogInfo("[Census] change unflushed for 30s (no beat) — "
                    + "carried by the next beat or N. Recurring = a beat lacks its OnBeat hook.");
            }
        }

        /// <summary>N: replay the last recorded change, past tense,
        /// unconditionally (CS1 design). Diffs first — keypress-freshness
        /// parity with the tables.</summary>
        public static void SpeakLast()
        {
            if (KnownBySurface.ContainsKey(Surface())) Diff();
            if (Last.Count == 0)
            {
                SpeechService.Say(Lex.T("census.none"), Priority.Immediate, "census");
                return;
            }
            var sb = new System.Text.StringBuilder(Lex.T("census.last-prefix"));
            for (int i = 0; i < Last.Count; i++)
                sb.Append(Past(Last[i])).Append(i < Last.Count - 1 ? ", " : ".");
            SpeechService.Say(sb.ToString(), Priority.Immediate, "census");
        }

        // ---------- Internals ----------

        private static string Present(Change c)
            => c.Name + " " + Lex.T(c.Appeared ? "census.appeared" : "census.gone");

        private static string Past(Change c)
            => c.Name + " " + Lex.T(c.Appeared ? "census.appeared-past" : "census.gone-past");

        private static void TryBaseline()
        {
            // Throttled and surface-gated (CS1 boot finding: per-frame Build
            // at the title scene spammed container-miss logs).
            if (Time.unscaledTime < _nextBaselineTry) return;
            _nextBaselineTry = Time.unscaledTime + Timing.CensusBaselineRetry;
            if (!OnStationSurface()) return;
            string surface = Surface();
            var snap = Snapshot();
            if (snap == null) { _candidate = null; return; }
            // Stability requirement (CS1 first-ride law: baselining mid-boot
            // recorded the station coming up as phantom appearances): lock
            // only when two consecutive snapshots agree — on the SAME surface.
            if (_candidate != null && _candidateSurface == surface
                && SameKeys(_candidate, snap))
            {
                _candidate = null;
                KnownBySurface[surface] = snap;
                Plugin.Log.LogInfo("[Census] baseline (" + surface + "): "
                    + snap.Count + " node(s), silent (stable x2).");
                return;
            }
            _candidate = snap;
            _candidateSurface = surface;
        }

        private static bool OnStationSurface()
        {
            var mode = ModeModel.Current();
            return mode == Mode.Station || mode == Mode.RigRooms
                || mode == Mode.ActionView;
        }

        private static bool SameKeys(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var k in a.Keys)
                if (!b.ContainsKey(k)) return false;
            return true;
        }

        private static bool WorldAtRest()
        {
            if (ConversationEvents.ConversationActive) return false;
            if (CycleGate.TransitionInFlight) return false;
            var mode = ModeModel.Current();
            return mode != Mode.CycleTransition && mode != Mode.Travel;
        }

        /// <summary>Key → display name off the zone atlas (camera-independent
        /// by construction). Null while the floor isn't ready — a real floor
        /// always lists locations (CS1 tighten): never baseline or diff a
        /// husk.</summary>
        private static Dictionary<string, string> Snapshot()
        {
            List<StationAtlas.Node> nodes;
            try { nodes = StationAtlas.Build(); }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[Census] snapshot failed: " + e.Message);
                return null;
            }
            if (nodes.Count == 0) return null;
            var snap = new Dictionary<string, string>();
            foreach (var n in nodes)
                if (!string.IsNullOrEmpty(n.Name)) snap["L:" + n.Name] = n.Name;
            return snap.Count > 0 ? snap : null;
        }

        private static void Diff()
        {
            // Diff strictly within the active surface's own set — an
            // un-baselined surface diffs nothing (its baseline path runs).
            string surface = Surface();
            if (!KnownBySurface.TryGetValue(surface, out var known)) return;
            var fresh = Snapshot();
            if (fresh == null) return;

            List<Change> changes = null;
            foreach (var kv in fresh)
                if (!known.ContainsKey(kv.Key))
                    (changes = changes ?? new List<Change>()).Add(
                        new Change { Key = kv.Key, Name = kv.Value, Appeared = true });
            foreach (var kv in known)
                if (!fresh.ContainsKey(kv.Key))
                    (changes = changes ?? new List<Change>()).Add(
                        new Change { Key = kv.Key, Name = kv.Value, Appeared = false });
            KnownBySurface[surface] = fresh;
            if (changes == null) return;

            // Fold into the pending batch; an oscillation (appeared then gone
            // before any flush) cancels to nothing rather than speaking a
            // phantom (CS1 law).
            foreach (var c in changes)
            {
                int opposite = Pending.FindIndex(
                    p => p.Key == c.Key && p.Appeared != c.Appeared);
                if (opposite >= 0) Pending.RemoveAt(opposite);
                else Pending.Add(c);
            }
            _pendingSince = Pending.Count > 0
                ? (_pendingSince > 0f ? _pendingSince : Time.unscaledTime) : -1f;
            _staleLogged = _staleLogged && Pending.Count > 0;

            Last.Clear();
            Last.AddRange(changes);
            var log = new System.Text.StringBuilder("[Census] change:");
            foreach (var c in changes)
                log.Append(' ').Append(c.Appeared ? '+' : '-').Append(c.Key);
            Plugin.Log.LogInfo(log.ToString());
        }

        /// <summary>Above this many same-direction changes the flush batches
        /// to a count (CS1: a district gate opening flips dozens at once — a
        /// name-by-name read at that scale is a wall; N carries detail).</summary>
        private const int BatchThreshold = 6;

        private static void TryFlush()
        {
            if (Pending.Count == 0) return;
            if (!OnStationSurface()) return;
            // Crisis ordering (owner ruling 2026-08-03: both outputs read,
            // toast first): while the game's own popup toast has triggered
            // but not yet drawn, the census tail stands down — once the
            // toast enqueues, queue order keeps toast-then-census. If the
            // beat window expires during the hold, the change carries to
            // the next beat or N (never lost, never over the toast).
            if (TutorialReader.PopupInFlight) return;
            EmitPending();
        }

        private static void EmitPending()
        {
            var sb = new System.Text.StringBuilder();
            int appeared = 0, gone = 0;
            foreach (var c in Pending) { if (c.Appeared) appeared++; else gone++; }
            if (appeared > BatchThreshold)
                sb.Append(appeared).Append(' ').Append(Lex.T("census.batch-appeared")).Append(' ');
            if (gone > BatchThreshold)
                sb.Append(gone).Append(' ').Append(Lex.T("census.batch-gone")).Append(' ');
            foreach (var c in Pending)
            {
                if (c.Appeared && appeared > BatchThreshold) continue;
                if (!c.Appeared && gone > BatchThreshold) continue;
                sb.Append(Present(c)).Append(' ');
            }
            SpeechService.Say(sb.ToString().TrimEnd(), Priority.Queued, "census");
            Pending.Clear();
            _pendingSince = -1f;
            _staleLogged = false;
        }
    }
}
