using UnityEngine;

using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    internal enum Mode
    {
        Title,
        Pause,
        Tutorial,
        Conversation,
        DiceAllocation,
        Map,
        DriveLog,
        CycleTransition,
        ActionView,
        Travel,
        RigRooms,
        Station,
    }

    /// <summary>The mode authority (CS1 ModeModel shape; every dial decoded —
    /// D4/D7/D8/D9/D11, 2026-07-31 — none guessed). Pure derivation, announces
    /// nothing, evaluated once per frame. Precedence top-down, CS1 model:
    /// pause outranks everything; rendered affordances (tutorial, conversation,
    /// dice allocation, map) outrank ambient wrappers (cycle transition);
    /// camera surfaces (action view, rig rooms, station) are the floor.
    ///
    /// Dials of record:
    ///  - Title: the MAIN MENU FSM exists in the loaded scene (level0 only).
    ///  - Pause: master PAUSE FSM ActiveStateName != "Idle" (D7; the alpha read
    ///    retired to fallback).
    ///  - Tutorial / Conversation: the mod's own trigger-owned surfaces.
    ///  - DiceAllocation: any of the three gamepad dice systems off Off/INIT (D11).
    ///  - Map: Map Screen root FSM in "Open" (D8).
    ///  - CycleTransition: Cycle Controller off "Idle" — its resting state has
    ///    FOUR exits in CS2 (player / travel / narrative auto-cycle / permadeath,
    ///    D4) so the wrapper arms on any departure.
    ///  - ActionView: the game's own Action View? global.
    ///  - Travel: Current Location Scene prefix "TRN" (D9 master scene dial).
    ///  - RigRooms: Ship UI toggle FSM in "Idle Ship" (D9 — RIG view is a
    ///    cutscene-restore flag, not this dial; Rig VIew? is a dead typo).
    /// </summary>
    internal static class ModeModel
    {
        private static int _frame = -1;
        private static Mode _cached = Mode.Station;

        public static Mode Current()
        {
            if (Time.frameCount == _frame) return _cached;
            _frame = Time.frameCount;
            _cached = Derive();
            return _cached;
        }

        public static string Name() => Current().ToString();

        private static Mode Derive()
        {
            if (GameQueries.TitleLive()) return Mode.Title;
            if (GameQueries.Paused()) return Mode.Pause;
            if (TutorialReader.Active()) return Mode.Tutorial;
            if (ConversationEvents.ConversationActive) return Mode.Conversation;
            if (GameQueries.DiceAllocationLive()) return Mode.DiceAllocation;
            if (GameQueries.MapOpen()) return Mode.Map;
            if (JournalTable.WindowOpen()) return Mode.DriveLog;
            if (GameQueries.CycleTransitioning()) return Mode.CycleTransition;
            if (ActionViewUp()) return Mode.ActionView;
            if (GameQueries.TravelScene()) return Mode.Travel;
            if (GameQueries.RigSide()) return Mode.RigRooms;
            return Mode.Station;
        }

        public static bool ActionViewUp()
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmBool("Action View?");
            return v != null && v.Value;
        }
    }
}
