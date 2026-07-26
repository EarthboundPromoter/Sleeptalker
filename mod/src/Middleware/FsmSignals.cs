using System;
using System.Collections.Generic;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Middleware
{
    /// <summary>
    /// The FSM state-entry event bus (build-plan W1): one Harmony hook on PlayMaker
    /// state entry feeding (fsm, state-name) subscriptions. This replaces per-feature
    /// 0.4s polling of ActiveStateName with push signals — outcomes (*_Outcome entry),
    /// dice commit (Slot Item / Select Dice 2), window lifecycles, cycle transitions.
    ///
    /// Signals are CLOCKS, never content (invariant 1): a subscriber may time an
    /// announcement off a state entry, but the words spoken must come from rendered
    /// text. Subscribers must be cheap and must not throw; a failing subscriber is
    /// muted after its first exception (graceful silence, invariant 5).
    /// </summary>
    internal static class FsmSignals
    {
        private sealed class Subscription
        {
            public string OwnerName;   // GameObject name of the FSM's owner; null = any
            public string StateName;   // exact state name; null = any state of that owner
            public Action<PlayMakerFSM, string> Handler;
            public bool Muted;
        }

        private static readonly List<Subscription> Subs = new List<Subscription>();

        /// <summary>Recent state entries, for dev inspection and live smoke tests.</summary>
        internal struct RingItem
        {
            public string OwnerName;
            public string StateName;
            public int Frame;
        }

        private const int RingSize = 128;
        private static readonly RingItem[] Ring = new RingItem[RingSize];
        private static long _entryCount;

        /// <summary>Total state entries observed since load — nonzero proves the hook is alive.</summary>
        public static long EntryCount => _entryCount;

        /// <summary>Subscribe to state entries. ownerName is the FSM owner GameObject's
        /// name (e.g. "Cycle Controller"); null matches any owner. stateName null matches
        /// any state. Handlers run inside the game's state switch — keep them cheap.</summary>
        public static void Subscribe(string ownerName, string stateName, Action<PlayMakerFSM, string> handler)
        {
            Subs.Add(new Subscription { OwnerName = ownerName, StateName = stateName, Handler = handler });
        }

        internal static void OnStateEntered(Fsm fsm, FsmState state)
        {
            // Never let anything below leak an exception into PlayMaker's state switch.
            try
            {
                string owner = fsm.GameObjectName;
                string stateName = state.Name;

                long n = _entryCount++;
                Ring[n % RingSize] = new RingItem
                    { OwnerName = owner, StateName = stateName, Frame = Time.frameCount };

                for (int i = 0; i < Subs.Count; i++)
                {
                    var sub = Subs[i];
                    if (sub.Muted) continue;
                    if (sub.OwnerName != null && sub.OwnerName != owner) continue;
                    if (sub.StateName != null && sub.StateName != stateName) continue;
                    try
                    {
                        if (Plugin.TraceFsmSignals.Value)
                            Plugin.Log.LogInfo("[FsmSignals] " + owner + " -> " + stateName);
                        sub.Handler(fsm.FsmComponent, stateName);
                    }
                    catch (Exception e)
                    {
                        sub.Muted = true;
                        Plugin.Log.LogWarning("[FsmSignals] Subscriber muted after exception ("
                            + (sub.OwnerName ?? "*") + "/" + (sub.StateName ?? "*") + "): " + e);
                    }
                }
            }
            catch
            {
                // Swallow: the game's state machine must never see our failure.
            }
        }

        /// <summary>Most-recent-first dump of the ring buffer (dev/log use only).</summary>
        public static List<RingItem> Recent(int max = 32)
        {
            var result = new List<RingItem>(max);
            long start = _entryCount - 1;
            for (long i = start; i >= 0 && i > start - max && i > start - RingSize; i--)
                result.Add(Ring[i % RingSize]);
            return result;
        }
    }

    /// <summary>Postfix so subscribers observe the state after its OnEnter actions ran —
    /// rendered text a state writes is already on screen when the signal fires.</summary>
    [HarmonyPatch(typeof(Fsm), "EnterState")]
    internal static class FsmEnterStatePatch
    {
        private static void Postfix(Fsm __instance, FsmState state)
        {
            if (__instance != null && state != null)
                FsmSignals.OnStateEntered(__instance, state);
        }
    }
}
