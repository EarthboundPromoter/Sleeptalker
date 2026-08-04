using System.Collections.Generic;
using UnityEngine;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The station census (P2; CS1 StationCensus.cs was the design
    /// of record — flush schedule REWORKED 2026-08-04, owner ruling,
    /// superseding the CS1 beat-tail port): spoken appearance/disappearance
    /// callouts for location markers, anywhere the zone atlas serves (hub,
    /// rig, contract floors — near-interchangeable surfaces for census
    /// purposes). The proper map (Map Screen) carries no census coverage.
    /// LOCATIONS ONLY: the CS1 character construction (Character Button /
    /// Click to Play canvases) does not exist in CS2 — verified against the
    /// census corpus before build (zero hits), owner request of record.
    ///
    /// Identity is the zone atlas key set (marker dial states, camera-
    /// independent — the CS1 trap: frustum churn must never read as change).
    /// Two channels:
    /// - Change callouts, PRESENT tense, EVENT-ANCHORED (owner ruling
    ///   2026-08-04, second pass — no schedule timers): a change landing
    ///   while the player is in control on the zone surface speaks at its
    ///   own settle (the post-churn diff); a change held while the player
    ///   was away (action view, dialogue, map, pause — the CS2 dials flip
    ///   ~10s+ after their causing beat, the 0.8.0 evening-log diagnosis)
    ///   COMPOSES ahead of the first node read after surface re-entry
    ///   ("X has appeared. NODE. ..."), with a loud standalone backstop if
    ///   no node read comes. Uniform boundary: nothing flushes inside a
    ///   location node's action view.
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
        private static float _pendingSince = -1f;
        private static float _onSurfaceSince = -1f;
        private static float _talkEndedAt = -1f;
        // Away epoch (delta-pass fix: rest-gate delays made a timestamp
        // classifier lie): a signal that fires off the zone surface marks
        // its eventual diff as away-born; pending also upgrades to away on
        // the surface-exit edge. Away pendings compose; present pendings
        // speak at settle.
        private static bool _dueAway;
        private static bool _pendingAway;
        private static bool _staleLogged;
        private static float _nextBaselineTry;

        public static void Init()
        {
            // Population signals: the marker dials' own boundary states — the
            // atlas Listed set is the arrival side, Off/Off 2 the removal
            // side. Filter = the marker anatomy (a Location Contents child),
            // never instance names (universal-hooks law). The game's own
            // new-location flash lives on the same dial family ("Clock
            // Flasher"/"Flashed Already?"), so the arrival moment IS a
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
            // Timestamp only (sync-pass finding, 2026-08-04): chained
            // conversations reopen within frames of ending — a grace past
            // the last end keeps a matured callout from firing into the gap,
            // where the next dialogue's queue-kill could destroy it unspoken.
            ConversationEvents.ConversationEnded +=
                () => _talkEndedAt = Time.unscaledTime;
        }

        private static void OnMarkerSignal(PlayMakerFSM fsm, string state)
        {
            if (fsm == null || fsm.gameObject == null) return;
            if (fsm.transform.Find("Location Contents") == null) return;
            // Coalesce bursts (a zone settle flips several markers): the diff
            // runs once, a beat after the churn quiets.
            _diffDueAt = Time.unscaledTime + Timing.CensusCoalesceDefer;
            if (!OnFlushSurface()) _dueAway = true;
        }

        /// <summary>Scene load: re-baseline silently — cross-load diffs are
        /// unknowable (CS1 law). Changes still pending at the load are lost
        /// with it; log the drop so the class stays observable.</summary>
        public static void OnSceneChanged()
        {
            if (Pending.Count > 0)
                Plugin.Log.LogInfo("[Census] " + Pending.Count
                    + " pending change(s) dropped at scene load (cross-load diffs unknowable).");
            KnownBySurface.Clear();
            _candidate = null;
            _candidateSurface = null;
            Pending.Clear();
            Last.Clear();
            _diffDueAt = -1f;
            _pendingSince = -1f;
            _onSurfaceSince = -1f;
            _dueAway = false;
            _pendingAway = false;
            _staleLogged = false;
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
                // The stamp is consumed only once a snapshot was actually
                // taken (sync-pass HIGH, 2026-08-04): with the zone surface
                // hidden (map/overlay open) Build() sees nothing and the
                // change would vanish from the scheduler — re-arm and retry
                // until the surface draws again.
                if (Diff()) _diffDueAt = -1f;
                else _diffDueAt = Time.unscaledTime + Timing.CensusCoalesceDefer;
            }
            // Surface-entry edge (event anchor, owner ruling second pass):
            // the mode transition INTO Station/RigRooms is the demarcation
            // for changes held while the player was away.
            bool onSurface = OnFlushSurface();
            if (onSurface && _onSurfaceSince < 0f)
                _onSurfaceSince = Time.unscaledTime;
            else if (!onSurface)
            {
                _onSurfaceSince = -1f;
                // Exit edge: anything still unspoken becomes news-from-away —
                // it composes with the returning node read.
                if (Pending.Count > 0) _pendingAway = true;
            }

            if (Pending.Count > 0 && _diffDueAt < 0f && onSurface
                && WorldAtRest() && !TutorialReader.PopupInFlight)
            {
                float now = Time.unscaledTime;
                if (!HeldFromAway())
                {
                    // The change landed while the player was present and in
                    // control: its own settle (the post-churn diff) is the
                    // event — speak now. The entry-settle guard keeps mode
                    // flickers from counting as presence.
                    if (now - _onSurfaceSince >= Timing.StationSurfaceSettle)
                        EmitPending();
                }
                else if (now >= _onSurfaceSince + Timing.StationSurfaceSettle
                                     + Timing.CensusComposeBackstop)
                {
                    // Held-from-away changes normally compose ahead of the
                    // first node read (ComposePrefix, pulled by the zone
                    // table and FocusPatch). No node read came — speak
                    // standalone, loudly.
                    Plugin.Log.LogWarning("[Census] no node read to compose"
                        + " with — spoken standalone (backstop)");
                    EmitPending();
                }
            }
            if (Pending.Count > 0 && _pendingSince > 0f && !_staleLogged
                && Time.unscaledTime - _pendingSince > Timing.CensusStaleLogAfter)
            {
                _staleLogged = true;
                Plugin.Log.LogInfo("[Census] change held for 30s (player off the "
                    + "zone surface) — flushes on surface return, or N. Expected "
                    + "during long action/dialogue stints.");
            }
        }

        /// <summary>N: replay the last recorded change, past tense,
        /// unconditionally (CS1 design). Diffs first — keypress-freshness
        /// parity with the tables.</summary>
        public static void SpeakLast()
        {
            // Freshness diff only at rest (sync-pass finding): a mid-flight
            // N must not record churning dials as phantom changes.
            if (KnownBySurface.ContainsKey(Surface()) && WorldAtRest()) Diff();
            if (Last.Count == 0)
            {
                SpeechService.Say(Lex.T("census.none"), Priority.Immediate, "census");
                return;
            }
            var sb = new System.Text.StringBuilder(Lex.T("census.last-prefix"));
            for (int i = 0; i < Last.Count; i++)
                sb.Append(Past(Last[i])).Append(i < Last.Count - 1 ? ", " : ".");
            SpeechService.Say(sb.ToString(), Priority.Immediate, "census");
            // A replayed change is heard (sync-pass finding): drop it from
            // the pending flush so the scheduler doesn't re-speak it in
            // present tense moments later. Older unreplayed pendings stand.
            foreach (var c in Last)
                Pending.RemoveAll(p => p.Key == c.Key && p.Appeared == c.Appeared);
            if (Pending.Count == 0) ClearPending();
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

        /// <summary>Where the census may OBSERVE (baseline/diff): any mode
        /// the atlas serves under, action view included — dials flip mid-
        /// action and the diff must capture them when they land.</summary>
        private static bool OnStationSurface()
        {
            var mode = ModeModel.Current();
            return mode == Mode.Station || mode == Mode.RigRooms
                || mode == Mode.ActionView;
        }

        /// <summary>Where the census may SPEAK (owner ruling 2026-08-04):
        /// actively on the hub/rig/contract zone surface — NOT inside a
        /// location node's action view (uniformity), not on the map, not
        /// under pause/dialogue/transition (those fail this or WorldAtRest).</summary>
        private static bool OnFlushSurface()
        {
            var mode = ModeModel.Current();
            return mode == Mode.Station || mode == Mode.RigRooms;
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
            // Post-conversation grace (sync-pass finding): chained dialogues
            // reopen within frames — the gap is not rest.
            if (_talkEndedAt > 0f && Time.unscaledTime
                < _talkEndedAt + Timing.CensusPostTalkGrace) return false;
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

        /// <summary>Returns true when a snapshot was actually taken (the
        /// caller may consume its due-stamp); false = surface not readable
        /// right now, try again.</summary>
        private static bool Diff()
        {
            // Diff strictly within the active surface's own set — an
            // un-baselined surface diffs nothing (its baseline path runs).
            string surface = Surface();
            if (!KnownBySurface.TryGetValue(surface, out var known)) return false;
            var fresh = Snapshot();
            if (fresh == null) return false;
            bool wasAway = _dueAway;
            _dueAway = false;

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
            if (changes == null) return true;

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
            if (Pending.Count > 0) _pendingAway = _pendingAway || wasAway;
            else _pendingAway = false;
            _pendingSince = Pending.Count > 0
                ? (_pendingSince > 0f ? _pendingSince : Time.unscaledTime) : -1f;
            _staleLogged = _staleLogged && Pending.Count > 0;

            Last.Clear();
            Last.AddRange(changes);
            var log = new System.Text.StringBuilder("[Census] change:");
            foreach (var c in changes)
                log.Append(' ').Append(c.Appeared ? '+' : '-').Append(c.Key);
            Plugin.Log.LogInfo(log.ToString());
            return true;
        }

        /// <summary>Above this many same-direction changes the flush batches
        /// to a count (CS1: a district gate opening flips dozens at once — a
        /// name-by-name read at that scale is a wall; N carries detail).</summary>
        private const int BatchThreshold = 6;

        /// <summary>True when the pending changes carry the away epoch —
        /// their signals fired off the zone surface, or the player left
        /// while they were unspoken. Those compose with the returning node
        /// read; present-born changes speak at their own settle.</summary>
        private static bool HeldFromAway() => _pendingAway;

        /// <summary>Pulled by FocusPatch at both node-read sites: a change
        /// held while the player was away composes AHEAD of the first node
        /// read after surface re-entry (owner lean, 2026-08-04) — one
        /// utterance, deterministic order. Returns null when there is
        /// nothing to compose or the moment is wrong; consuming clears the
        /// pending batch (N keeps the past-tense replay).
        /// Crisis ordering (owner ruling 2026-08-03: both outputs read,
        /// toast first) rides the PopupInFlight gate here and in Tick —
        /// the callout carries until the toast is through, never over it.</summary>
        public static string ComposePrefix()
        {
            if (!OnFlushSurface() || !WorldAtRest()) return null;
            if (TutorialReader.PopupInFlight) return null;
            // Catch-up diff (delta-pass fix: the map-close re-arm cycle can
            // leave the due-stamp maturing when the first node read fires —
            // the compose must not lose the race to its own coalesce; the
            // read moment is itself a settle point, and oscillation folding
            // still scrubs any half-flipped world).
            if (_diffDueAt > 0f && Diff()) _diffDueAt = -1f;
            if (Pending.Count == 0 || _diffDueAt > 0f) return null;
            if (!HeldFromAway()) return null;
            var text = PendingText();
            ClearPending();
            Plugin.Log.LogInfo("[Census] callout composed ahead of the node read");
            return text;
        }

        private static void EmitPending()
        {
            SpeechService.Say(PendingText(), Priority.Queued, "census");
            ClearPending();
        }

        private static void ClearPending()
        {
            Pending.Clear();
            _pendingSince = -1f;
            _pendingAway = false;
            _staleLogged = false;
        }

        private static string PendingText()
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
            return sb.ToString().TrimEnd();
        }
    }
}
