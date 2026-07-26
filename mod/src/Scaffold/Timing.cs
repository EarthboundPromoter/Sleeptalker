namespace Sleeptalker.Scaffold
{
    /// <summary>The mod's tuned delays and windows in one place (audit C4): the
    /// implicit timing model, made visible and CS2-retunable. Every value here was
    /// live-calibrated against CS1 render behavior; the provenance for each stays
    /// at its call site. Constants that were already named locally before the
    /// consolidation pass (Watchers.Interval, FocusPatch.FocusSettle, MapTable's
    /// RefreshInterval/OscillationWindow, CloudFlight's) remain in place — this
    /// registry collects the knobs that were bare literals.</summary>
    internal static class Timing
    {
        // ---------- Polling cadences ----------
        public const float WindowStatePoll = 5f;        // quest-log search + divergence check
        public const float SelfTestRetry = 5f;
        public const float EndgameFindPoll = 3f;
        public const float PointsPoll = 0.5f;           // Lua points poll (no render loop while window closed)
        public const float CensusBaselineRetry = 2f;

        // ---------- Speech pacing ----------
        public const float SpeechQueueGap = 0.15f;      // min gap between queued utterances

        // ---------- Focus / navigation ----------
        public const float UserMoveWindow = 0.25f;      // selection changes this fresh count as user-initiated
        public const float StripReanchorCooldown = 1.5f;
        public const float ReanchorDefer = 0.1f;
        public const float ItemNameSettle = 0.15f;      // Inventory Display writes the new item's name
        public const float ActivateDebounce = 0.3f;     // second click mid-script breaks tutorial chains
        public const float BootQuietWindow = 8f;        // empty-world reads are boot noise, not news

        // ---------- Dice flow ----------
        public const float DiceModeHysteresis = 0.25f;  // hold allocation mode across a one-frame null read
        public const float OutlookSettleBail = 0.6f;    // odds read: boost animation long done by now
        public const float DiceSpendWindow = 1.5f;      // controller-left-Idle spend heuristic
        public const float RerollRereadDelay = 1.0f;    // dice re-render, then the new pool

        // ---------- Outcome / effect pipeline ----------
        public const float OutcomeCardDefer = 0.15f;    // card content read after the tier announce
        public const float EffectEchoWindow = 2f;       // effect-line dedupe (Watchers writes, LocationTable reads)
        public const float ResourceStandDownWindow = 2.5f; // ambient lane yields to a fresh interaction voice
        public const float ResourceSayDefer = 1.2f;
        public const float RefusalSayDefer = 0.1f;
        public const float CryoComposeSuppress = 4f;    // notification stands down after the cloud compose spoke it

        // ---------- Vitals watchers (ConditionWatch shape, shared by design) ----------
        public const float StatEventDefer = 0.2f;       // one-shot events (breakdown, starving)
        public const float BandLabelSettle = 0.3f;      // band change: let the localized label land

        // ---------- Cloud ----------
        public const float CloudSurfaceSettle = 1.2f;   // land AFTER the entry census's Text Setup settle
        public const float CloudEntryCensusSettle = 0.8f;
        public const float CloudRevealDiffDefer = 0.6f; // target dials flip on everyFrame watches + Text Setup
        public const float CloudOutcomeAnimDefer = 0.35f;
        public const float CloudCompleteDefer = 0.2f;
        public const float TrackerLineDefer = 0.6f;     // post-increment dial value after the outcome read
        public const float HackComposeDefer = 0.3f;     // lets a same-beat perk proc ride the composed string
        public const float PerkProcWindow = 1.5f;
        public const float CloudRepeatWindow = 6f;      // two clocks fire per resolve; identical content once

        // ---------- Station tables ----------
        public const float StationSurfaceSettle = 0.6f; // transitional mode flickers must not announce
        public const float CommitEnableWait = 2f;       // camera-in-flight: wait for the button, refuse on timeout
        public const float CameraFocusMute = 1.5f;      // focus mute after a Focus Z write
        public const float PurchaseCheckDefer = 0.3f;   // deferred post-activation read (re-armed while Confirm? is up)
        public const float JournalRetryDefer = 0.2f;    // pull-out renders a beat after the heading click
        public const float JournalResultDefer = 0.25f;

        // ---------- Census ----------
        public const float CensusCoalesceDefer = 0.5f;  // burst of canvas flips diffs once after the churn quiets
        public const float CensusBeatWindow = 3f;
        public const float CensusFlushLeadIn = 1.5f;    // canvases' return-to-listed may cancel a phantom first
        public const float CensusStaleLogAfter = 30f;

        // ---------- Log throttles ----------
        public const float BothWindowsLogThrottle = 30f;
    }
}
