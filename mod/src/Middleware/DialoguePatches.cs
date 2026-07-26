using System.Collections.Generic;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Middleware
{
    /// <summary>Tracks the currently offered dialogue responses so number keys can pick them.</summary>
    internal static class DialogueState
    {
        public static readonly List<string> CurrentResponses = new List<string>();
        public static bool MenuOpen;
        public static string LastSubtitle = "";
        public static string LastSpeaker = "";
        /// <summary>U2 (user report, 0.8): the speaker last ANNOUNCED by name this
        /// conversation — auto-reads prefix the name only when it changes (window
        /// open, or a genuine mid-window switch: Toshiro→Sabine exists). Unnamed
        /// lines (environmental headers) don't clear it. Reset at conversation start.</summary>
        public static string LastAnnouncedSpeaker = "";
        /// <summary>Incremented per subtitle so the watcher can announce a sole Continue option.</summary>
        public static long SubtitleSeq;

        /// <summary>Per-conversation subtitle history (speaker, line) for the B
        /// dialogue-log review's attribution backfill (owner design 2026-07-23):
        /// the rendered log blocks are bare text — the mod remembers who spoke each
        /// line as it was live. Cleared at conversation start; capped defensively.</summary>
        public static readonly List<(string Speaker, string Text)> History =
            new List<(string Speaker, string Text)>();

        public static void SetResponses(Response[] responses)
        {
            CurrentResponses.Clear();
            if (responses != null)
                foreach (var r in responses)
                    CurrentResponses.Add(r?.formattedText?.text ?? "");
            MenuOpen = CurrentResponses.Count > 0;
        }
    }

    internal static class DialogueAnnouncer
    {
        public static void OnSubtitle(Subtitle subtitle)
        {
            var text = subtitle?.formattedText?.text;
            if (string.IsNullOrEmpty(text)) return;
            var speaker = subtitle.speakerInfo?.Name;
            DialogueState.LastSubtitle = text;
            DialogueState.LastSpeaker = speaker ?? "";
            DialogueState.MenuOpen = false;
            DialogueState.SubtitleSeq++;
            DialogueState.History.Add((speaker ?? "", text));
            if (DialogueState.History.Count > 400) DialogueState.History.RemoveAt(0);

            if (!Plugin.AutoReadDialogue.Value) return;
            bool named = !string.IsNullOrEmpty(speaker) && Plugin.SpeakSpeakerNames.Value &&
                         !speaker.Equals("UNKNOWN", System.StringComparison.OrdinalIgnoreCase);
            // U2: name on change only, never on every advance.
            bool sayName = named && !speaker.Equals(DialogueState.LastAnnouncedSpeaker,
                System.StringComparison.OrdinalIgnoreCase);
            if (sayName) DialogueState.LastAnnouncedSpeaker = speaker;
            SpeechService.Say(sayName ? speaker + ": " + text : text,
                Priority.Queued, "dialogue");
        }

        public static void OnResponses(Response[] responses)
        {
            DialogueState.SetResponses(responses);
            int n = DialogueState.CurrentResponses.Count;
            if (n == 0) return;
            // Just the count; the focused choice announces itself (game auto-selects one,
            // or the watcher focuses the first). Arrows and number keys cover the rest.
            SpeechService.Say(n == 1 ? "1 choice." : n + " choices.", Priority.Queued, "choices");
        }
    }

    [HarmonyPatch(typeof(UnityUIDialogueUI), nameof(UnityUIDialogueUI.ShowSubtitle))]
    internal static class UnityUiSubtitlePatch
    {
        private static void Postfix(Subtitle subtitle) => DialogueAnnouncer.OnSubtitle(subtitle);
    }

    [HarmonyPatch(typeof(StandardDialogueUI), nameof(StandardDialogueUI.ShowSubtitle))]
    internal static class StandardSubtitlePatch
    {
        private static void Postfix(Subtitle subtitle) => DialogueAnnouncer.OnSubtitle(subtitle);
    }

    [HarmonyPatch(typeof(UnityUIDialogueUI), nameof(UnityUIDialogueUI.ShowResponses))]
    internal static class UnityUiResponsesPatch
    {
        private static void Postfix(Response[] responses) => DialogueAnnouncer.OnResponses(responses);
    }

    [HarmonyPatch(typeof(StandardDialogueUI), nameof(StandardDialogueUI.ShowResponses))]
    internal static class StandardResponsesPatch
    {
        private static void Postfix(Response[] responses) => DialogueAnnouncer.OnResponses(responses);
    }

    [HarmonyPatch(typeof(UnityUIDialogueUI), nameof(UnityUIDialogueUI.ShowAlert))]
    internal static class UnityUiAlertPatch
    {
        private static void Postfix(string message) =>
            SpeechService.Say(message, Priority.Queued, "alert");
    }

    [HarmonyPatch(typeof(StandardDialogueUI), nameof(StandardDialogueUI.ShowAlert))]
    internal static class StandardAlertPatch
    {
        private static void Postfix(string message) =>
            SpeechService.Say(message, Priority.Queued, "alert");
    }
}
