using PixelCrushers.DialogueSystem;
using UnityEngine;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Middleware
{
    /// <summary>Event-driven conversation truth (W2 hardening, owner design): subscribes
    /// to the Dialogue System's own conversationStarted/conversationEnded — fired by the
    /// same code path that creates conversations, so no trigger route (player input,
    /// on-leave triggers, Ink story beats) can bypass them. Reliable by construction;
    /// replaces polling currentConversationState in the mode model.</summary>
    internal static class ConversationEvents
    {
        public static bool ConversationActive { get; private set; }

        private static DialogueSystemController _subscribed;

        public static void Tick()
        {
            // The controller is a scene object; resubscribe if it was recreated.
            var controller = DialogueManager.instance;
            if (!ReferenceEquals(controller, _subscribed))
            {
                if (controller != null)
                {
                    controller.conversationStarted += OnStarted;
                    controller.conversationEnded += OnEnded;
                    Plugin.Log.LogInfo("[Dialogue] conversation lifecycle events subscribed.");
                }
                _subscribed = controller;
                ConversationActive = false;
            }
        }

        private static void OnStarted(Transform actor)
        {
            ConversationActive = true;
            // U2: a fresh window announces its first named speaker once.
            DialogueState.LastAnnouncedSpeaker = "";
            // The dialogue-log attribution history is per-conversation, like the
            // rendered log itself.
            DialogueState.History.Clear();
        }

        /// <summary>A conversation's end is a census beat: dialogue is the canonical
        /// writer of the story flags that spawn/remove markers. Event, not a game-tier
        /// call — the CS1 TIER-X reference, inverted from day one here; the census
        /// subscribes at the composition root when it exists.</summary>
        public static System.Action ConversationEnded = () => { };

        private static void OnEnded(Transform actor)
        {
            ConversationActive = false;
            try { ConversationEnded(); } catch { }
        }
    }
}
