using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;
using Sleeptalker.Sleeper2;
using Priority = Sleeptalker.Scaffold.Priority;

namespace Sleeptalker
{
    [BepInPlugin(Id, "Sleeptalker", "0.9.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.sleeptalker.mod";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> AutoReadDialogue;
        internal static ConfigEntry<bool> SpeakSpeakerNames;
        internal static ConfigEntry<bool> AnnounceFocus;
        internal static ConfigEntry<bool> ForceGamepadUI;
        internal static ConfigEntry<bool> TraceFsmSignals;
        internal static ConfigEntry<bool> TraceInput;

        private InputManager _input;

        private void Awake()
        {
            Log = Logger;
            Application.runInBackground = true;

            AutoReadDialogue = Config.Bind("Speech", "AutoReadDialogue", true,
                "Automatically read dialogue lines as they appear.");
            SpeakSpeakerNames = Config.Bind("Speech", "SpeakSpeakerNames", true,
                "Announce the dialogue speaker's name when it changes (window open "
                + "or a mid-conversation switch), not on every advance.");
            AnnounceFocus = Config.Bind("Speech", "AnnounceFocus", true,
                "Announce UI elements when they receive focus.");
            ForceGamepadUI = Config.Bind("Input", "ForceGamepadUI", true,
                "Keep the game's UI in gamepad mode so the keyboard flow works.");
            TraceFsmSignals = Config.Bind("Debug", "TraceFsmSignals", false,
                "Log every subscribed FSM state-entry dispatch (dev diagnostics).");
            TraceInput = Config.Bind("Debug", "TraceInput", false,
                "Log every mod-relevant key press and synthetic nav event live "
                + "(always recorded in memory for the F3 incident dump regardless).");

            SpeechService.Init();
            Lex.Init();
            // Modal-primacy source classes (owner design 2026-07-26): these lines
            // regenerate fresh when a modal dismisses, so under one they drop
            // rather than hold. Every unlisted source holds and replays (durable
            // default — never silently lose a future watcher's line).
            SpeechService.RegisterVolatile("focus");
            SpeechService.RegisterVolatile("nav");
            SpeechService.RegisterVolatile("odds");

            // Tier seams: the scaffold speaks and diagnoses through injected
            // providers; this composition root is the only place that wires
            // game-tier knowledge downward (CS1 stage-1 discipline, kept).
            ElementDescriber.Element = Describe.Element;
            NavSignals.UserNavigated = FocusPatch.NoteUserNavigation;
            NavSignals.ClearCooldown = FocusPatch.ClearCooldown;
            // The real ModeModel (2026-07-31) — the 3-value stub is retired.
            Diag.ModeNameProvider = ModeModel.Name;
            Diag.StateProvider = DiagState;
            Diag.FsmRecentProvider = n =>
            {
                var list = new System.Collections.Generic.List<(int, string, string)>();
                foreach (var r in FsmSignals.Recent(n))
                    list.Add((r.Frame, r.OwnerName, r.StateName));
                return list;
            };

            var harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                FocusPatch.OnSceneChanged();
                StationAtlas.InvalidateScene();
                GameQueries.InvalidateScene();
                ZoneTable.OnSceneChanged();
                MapTable.OnSceneChanged();
                InventoryTable.OnSceneChanged();
                JournalTable.InvalidateScene();
                TutorialReader.OnSceneChanged();
                StationCensus.OnSceneChanged();
            };

            _input = new InputManager();
            TitleFlow.Init();
            ClassSelect.Init();
            SkillChecks.Init();
            Vitals.Init();
            TutorialReader.Init();
            ZoneTable.Init();
            MapTable.Init();
            InventoryTable.Init();
            CharacterTable.Init();
            DiceFlow.Init();
            CrewPanel.Init();
            TopBarTable.Init();
            ActionOutcomes.Init();
            PushFlow.Init();
            CycleGate.Init();
            StationCensus.Init();

            Log.LogInfo("Sleeptalker 0.9.0 loaded.");
            SpeechService.Say("Sleeptalker 0.9.0.", Priority.Queued, "init");
        }

        private void Update()
        {
            _input.Tick();
            FocusPatch.Tick();
            TitleFlow.Tick();
            ClassSelect.Tick();
            TutorialReader.Tick();
            ZoneTable.Tick();
            MapTable.Tick();
            InventoryTable.Tick();
            DiceFlow.Tick();
            CharacterTable.Tick();
            GuideReader.Tick();
            LocationTable.Tick();
            TopBarTable.Tick();
            ActionOutcomes.Tick();
            PushFlow.Tick();
            Vitals.Tick();
            CycleGate.Tick();
            StationCensus.Tick();
            JournalTable.Tick();
            DialogueColumn.Tick();
            Navigator.Tick();
            SpeechService.Tick();
            ConversationEvents.Tick();
        }

        /// <summary>Diag's believed-state entries (keys = the /modstate contract).</summary>
        private static System.Collections.Generic.Dictionary<string, object> DiagState()
        {
            var state = new System.Collections.Generic.Dictionary<string, object>();
            try { state["conversation"] = ConversationEvents.ConversationActive; } catch { }
            try { state["responseMenu"] = DialogueState.MenuOpen; } catch { }
            return state;
        }

        private void OnDestroy()
        {
            SpeechService.Shutdown();
        }
    }
}
