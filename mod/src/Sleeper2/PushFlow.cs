using System.Collections.Generic;
using UnityEngine;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>The push ability voice (decode D20, owner ruling batch
    /// 2026-08-02 — the push vocabulary session).
    ///
    /// The usage machine is ONE FSM: Top UI/Push System (D20). Its resting
    /// state is the availability dial; its three execution entry states are
    /// named for the class abilities. The game renders NO outcome text —
    /// the only push language on screen is the config sentence panel plus
    /// the gamepad confirm prompt — so the fire read is a mod composition
    /// in the outcome-lane shape: "Push." + per-die clauses from the drawn
    /// faces + stress movements in the ruled delta grammar (bare change =
    /// "label up N, now total"; adjusted change = adjustment, factor, total).
    ///
    /// Rulings of record (all 2026-08-02):
    ///  - P is TWO-PRESS through the FSM's own event ladder (the game's
    ///    confirmed-push channel; the raw button click bypasses the confirm
    ///    stage and is retired from both entry paths). First press arms and
    ///    the rendered confirm box is spoken as a modal capture; second
    ///    press fires. The 4s native window lapsing speaks a disarm.
    ///  - A dead press speaks its reason from the state dial (used this
    ///    cycle / stress full / hub) — the denial-composition precedent,
    ///    not the G silence. The Disabled/CRT? reasons disambiguate via
    ///    Current Location Scene (variable lane, logged: the button dims
    ///    identically for every reason — sprite-only render escape).
    ///  - Fire clauses per class: rerolled (REBOOT), boosted (RALLY, by
    ///    rendered crew name), focused (FOCUS).
    ///  - The REBOOT node-3 refund (−2 on a pushed 6) is attributed with
    ///    the factor tag "reroll six" inside the same utterance.
    ///
    /// Hooks are universal (the class entry states), so a native pad/mouse
    /// push speaks exactly like a P-key push.</summary>
    internal static class PushFlow
    {
        // FSM 29162 state vocabulary (D20 §a).
        private static readonly HashSet<string> ArmedStates =
            new HashSet<string> { "OPR", "XTR", "MHT" };
        private static readonly HashSet<string> ConfirmStates = new HashSet<string>
        {
            // Gamepad confirm stage + the mouse hover stage (both carry a
            // Push transition; the mod rides whichever the Gamepad dial picks).
            "REBOOT 01", "REBOOT 02", "RALLY 01", "RALLY 02", "FOCUS 03", "FOCUS 04",
        };
        private const string ExecReboot = "REBOOT (REROLL DICE)";
        private const string ExecRally = "RALLY (+ CREW DICE)";
        private const string ExecFocus = "FOCUS (+ YOUR DICE)";

        private sealed class Fire
        {
            public string Class;            // exec state name
            public float Deadline;          // fail-loud backstop
            public int Ticks;
            public int GraceTicks;          // post-settle beat for the bar-poll
                                            // deltas (sync pass MED-8)
            public float[] CrewBefore;      // RALLY diff base (member*2+slot)
            public bool[] FocusedBefore;    // FOCUS stale-marker filter (MED-7)
        }

        private static Fire _fire;
        private static bool _costPending;       // the push cost's delta hasn't
                                                // reached the composition yet
                                                // (log finding 2026-08-02: fire
                                                // #1 spoke without its stress
                                                // clause — the window closed
                                                // before the bar's poll landed)
        private static bool _armWatch;          // a confirm stage is up
        private static bool _modArmed;          // WE armed it (sync pass MED-4/5:
                                                // native mouse hover enters the same
                                                // states — cancel/disarm speech only
                                                // for arms the keyboard initiated)
        private static float _confirmSpokeAt = -10f;
        private static readonly List<string> _deltas = new List<string>();
        private static readonly HashSet<int> _rerolled = new HashSet<int>();
        private static readonly HashSet<int> _focused = new HashSet<int>();
        private static bool _regainSeen;

        /// <summary>The push lane is composing: Vitals hands observed deltas
        /// here instead of speaking ambient lines (the outcome-lane shape).</summary>
        public static bool FireInFlight => _fire != null;

        public static void Init()
        {
            // Confirm stage entered (ours OR a native pad/mouse arm): the
            // rendered box is the announce — a modal capture, buttons excluded.
            foreach (var state in ConfirmStates)
                FsmSignals.Subscribe("Push System", state, (fsm, s) => OnConfirmStage(fsm));

            // Fire: the class entry states, entered exactly once per push
            // (D20 §g). The exec graph settles in frames — results are read
            // from the RECEIVING FSMs at settle, never the root's transients.
            FsmSignals.Subscribe("Push System", ExecReboot, (fsm, s) => BeginFire(fsm, s));
            FsmSignals.Subscribe("Push System", ExecRally, (fsm, s) => BeginFire(fsm, s));
            FsmSignals.Subscribe("Push System", ExecFocus, (fsm, s) => BeginFire(fsm, s));

            // Which dice the push actually touched, from the die FSMs' own
            // states (owner "Die"): Roll/Safe Roll = rerolled (REBOOT; both
            // fire on normal cycle rolls too — gated on the fire window),
            // Focus Viz = the focused die (FOCUS), Regain Stress = the node-3
            // refund moment (its factor tag).
            FsmSignals.Subscribe("Die", "Roll", (fsm, s) => NoteDie(fsm, _rerolled));
            FsmSignals.Subscribe("Die", "Safe Roll", (fsm, s) => NoteDie(fsm, _rerolled));
            FsmSignals.Subscribe("Die", "Focus Viz", (fsm, s) => NoteDie(fsm, _focused));
            // BOTH refund states (sync pass HIGH-3: the non-safe Roll path
            // refunds through "Regain Stress 2" — node 3 without node 1 is
            // an ordinary config and took the unsubscribed chain).
            FsmSignals.Subscribe("Die", "Regain Stress", (fsm, s) =>
            {
                if (_fire != null) _regainSeen = true;
            });
            FsmSignals.Subscribe("Die", "Regain Stress 2", (fsm, s) =>
            {
                if (_fire != null) _regainSeen = true;
            });

            // Deferred FOCUS heal (owner ruling: attributed systematically):
            // the Action Controller's own heal rungs at outcome resolution —
            // the stress delta the standing-down channel hands to the outcome
            // lane carries the "focus" factor tag.
            FsmSignals.Subscribe(null, "Boon Stress Heal", (fsm, s) => NoteFocusHeal(fsm));
            FsmSignals.Subscribe(null, "Neu Stress Heal", (fsm, s) => NoteFocusHeal(fsm));
        }

        private static void NoteFocusHeal(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject == null) return;
            var id = fsm.FsmVariables.GetFsmString("Action Identifier");
            if (id == null || string.IsNullOrEmpty(id.Value)) return;
            ActionOutcomes.NoteFactor(Lex.T("vitals.stress"), Lex.T("push.factor-focus"));
        }

        /// <summary>P (and the V-row PUSH cell's Enter): drive the FSM's own
        /// two-press ladder from whatever state it rests in. Never the raw
        /// button click — that bypasses the confirm stage (D20 §g).</summary>
        public static void Key()
        {
            var fsm = SystemFsm();
            if (fsm == null)
            {
                Plugin.Log.LogInfo("[Push] no Push System FSM alive — silent");
                return;
            }
            string state = fsm.ActiveStateName ?? "";
            if (ArmedStates.Contains(state))
            {
                // Press 1 = the same event the game's own hover/stick press
                // sends; the FSM raises the panel and (gamepad dial) the
                // confirm prompt — the state-entry hook speaks the box.
                _modArmed = true;
                fsm.SendEvent("Mouseover");
                return;
            }
            if (ConfirmStates.Contains(state))
            {
                // Press 2 = the confirm stage's own fire transition.
                fsm.SendEvent("Push");
                return;
            }
            SayDeadReason(state);
        }

        /// <summary>A keyboard-initiated confirm stage is up — Enter routes
        /// here instead of the native activate (sync pass HIGH-1: the V-row
        /// commit exits the table, so the second Enter needs its own rung).</summary>
        public static bool ModArmed()
        {
            if (!_modArmed) return false;
            var fsm = SystemFsm();
            return fsm != null && ConfirmStates.Contains(fsm.ActiveStateName ?? "");
        }

        /// <summary>Backspace rung: an armed confirm cancels through the
        /// game's own Mouseoff (the pointer-exit event the stage already
        /// listens for). Only for arms WE initiated (sync pass MED-4: a
        /// native mouse hover holds the same states — Backspace must not be
        /// stolen from the mode ladder for someone else's pointer).</summary>
        public static bool CancelArm()
        {
            if (!ModArmed()) return false;
            SystemFsm().SendEvent("Mouseoff");
            return true;
        }

        /// <summary>Dead-press reasons from the state dial (owner ruling).
        /// Disabled draws identically for stress-full and hub; the scene
        /// name is the game's own discriminator (variable lane, logged).</summary>
        private static void SayDeadReason(string state)
        {
            string key;
            switch (state)
            {
                case "USED":
                    key = "push.used";
                    break;
                case "Disabled":
                    // Machinist node 5 moves the gate to the glitch track
                    // (sync pass LOW-11): the cost-resource global names the
                    // full meter.
                    key = AtHub() ? "push.hub"
                        : GlitchCost() ? "push.glitch-full" : "push.stress-full";
                    Plugin.Log.LogInfo("[Push] Disabled reason via scene var: " + key);
                    break;
                case "CRT?":
                    key = AtHub() ? "push.hub" : "push.unavailable";
                    break;
                default:
                    Plugin.Log.LogInfo("[Push] dead press in state '" + state
                        + "' — generic reason");
                    key = "push.unavailable";
                    break;
            }
            SpeechService.Say(Lex.T(key), Priority.Queued, "push");
        }

        private static bool AtHub()
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmString("Current Location Scene");
            return v != null && v.Value != null && v.Value.Contains("HUB");
        }

        private static bool GlitchCost()
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables
                .GetFsmString("Push Cost Resource");
            return v != null && v.Value == "Player_Glitch";
        }

        /// <summary>The confirm box, spoken as rendered (owner ruling: capture
        /// the box like any confirm modal): the active class panel's texts in
        /// screen order with the confirm prompt hoisted LAST (the hierarchy
        /// parks the prompt above the sentence). Slash-run separators inside
        /// the sentence transcode to sentence breaks; leading +/- glyph runs
        /// transcode via the shared parse (never stripped).</summary>
        private static void OnConfirmStage(PlayMakerFSM fsm)
        {
            _armWatch = true;
            if (Time.unscaledTime - _confirmSpokeAt < 0.75f) return; // hover flap dedupe
            _confirmSpokeAt = Time.unscaledTime;

            var body = new List<string>();
            var prompt = new List<string>();
            var panel = ActivePanel(fsm);
            if (panel != null)
            {
                foreach (var tmp in panel.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (!Util.RenderedUp(tmp.transform)) continue;
                    string text = SpeechService.Clean(tmp.text);
                    if (string.IsNullOrEmpty(text)) continue;
                    bool isPrompt = false;
                    for (var t = tmp.transform; t != null && t != panel; t = t.parent)
                        if (t.name.StartsWith("Push Gamepad")) { isPrompt = true; break; }
                    (isPrompt ? prompt : body).Add(Transcode(text));
                }
            }
            var sb = new System.Text.StringBuilder();
            foreach (var line in body) Append(sb, line);
            foreach (var line in prompt) Append(sb, line);
            if (sb.Length == 0)
            {
                // The stage is up but nothing readable drew — never silent.
                Plugin.Log.LogWarning("[Push] confirm stage renders no readable text");
                sb.Append(Lex.T("push.confirm-bare"));
            }
            SpeechService.Say(sb.ToString(), Priority.Queued, "push");
        }

        private static Transform ActivePanel(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.transform == null) return null;
            foreach (Transform child in fsm.transform)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                string n = child.name;
                if (n.StartsWith("REBOOT") || n.StartsWith("RALLY") || n.StartsWith("FOCUS"))
                    return child;
            }
            return null;
        }

        /// <summary>The game's slash-run clause separator (" // ") becomes a
        /// sentence break; a leading +/- glyph run speaks through the shared
        /// GlyphRun parse (transcode-not-strip).</summary>
        private static string Transcode(string text)
        {
            var parts = text.Split(new[] { "//" }, System.StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var raw in parts)
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                if (part[0] == '+' || part[0] == '-')
                {
                    int body = Util.GlyphRun(part, out int plus, out int minus);
                    string rest = part.Substring(body).Trim();
                    if (rest.Length > 0)
                    {
                        if (plus > 0 && minus == 0)
                            part = Lex.T("glyph.plus") + (plus > 1 ? " " + plus : "") + " " + rest;
                        else if (minus > 0 && plus == 0)
                            part = Lex.T("glyph.minus") + (minus > 1 ? " " + minus : "") + " " + rest;
                    }
                }
                Append(sb, part);
            }
            return sb.ToString();
        }

        private static void Append(System.Text.StringBuilder sb, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(line);
            char last = line[line.Length - 1];
            if (last != '.' && last != '!' && last != '?') sb.Append('.');
        }

        private static void BeginFire(PlayMakerFSM fsm, string execState)
        {
            _armWatch = false;
            _modArmed = false;
            _deltas.Clear();
            _rerolled.Clear();
            _focused.Clear();
            _regainSeen = false;
            // ForceUnslotDice snaps slotted dice home at exec entry — the
            // dice reader's pending "Die returned." verify would speak into
            // the fire window (sync pass MED-6).
            DiceFlow.DropPendingReturns("push fire");
            // Every push pays its cost at Set Stress — the window holds open
            // until that delta arrives (or the deadline), so the cost clause
            // can never miss the composition again.
            _costPending = true;
            var fire = new Fire
            {
                Class = execState,
                Deadline = Time.unscaledTime + 2f,
            };
            if (execState == ExecRally)
            {
                fire.CrewBefore = new float[4];
                for (int m = 1; m <= 2; m++)
                    for (int s = 1; s <= 2; s++)
                        fire.CrewBefore[(m - 1) * 2 + (s - 1)] = CrewDieValue(m, s);
            }
            if (execState == ExecFocus)
            {
                // A still-focused die from an earlier cycle re-enters Focus
                // Viz on the fire's own Check Variables ping (sync pass
                // MED-7) — only NEW entrants read as this push's work.
                fire.FocusedBefore = new bool[5];
                for (int n = 1; n <= 5; n++)
                    fire.FocusedBefore[n - 1] = DieFocusedBool(n);
            }
            _fire = fire;
        }

        private static bool DieFocusedBool(int n)
        {
            var go = GameObject.Find("Letterbox Canvas/Top UI/Dice UI/Dice Slot " + n + "/Die");
            var fsm = go != null ? go.GetComponent<PlayMakerFSM>() : null;
            var b = fsm != null ? fsm.FsmVariables.GetFsmBool("Focused") : null;
            return b != null && b.Value;
        }

        private static void NoteDie(PlayMakerFSM fsm, HashSet<int> into)
        {
            if (_fire == null || fsm == null) return;
            for (var t = fsm.transform; t != null; t = t.parent)
            {
                if (!t.name.StartsWith("Dice Slot ")) continue;
                int n = (int)Util.LeadingInt(t.name.Substring(10));
                if (n > 0) into.Add(n);
                return;
            }
        }

        /// <summary>Vitals hand-off during the fire window (the outcome-lane
        /// shape): composed here in the ruled delta grammar — a stress DROP
        /// while the die FSM's Regain Stress fired carries the "reroll six"
        /// factor; everything else is the bare adjustment + standing total.</summary>
        public static void OfferDelta(string label, float delta, string nowFormatted)
        {
            if (Mathf.Approximately(delta, 0f) || string.IsNullOrEmpty(label)) return;
            // The cost resource's delta arrived — the compose gate opens.
            // Exact label match: crew stress labels CONTAIN "Stress" and must
            // not satisfy the cost latch.
            if (label == Lex.T("vitals.stress") || label == Lex.T("vitals.glitch"))
                _costPending = false;
            string sign = Lex.T(delta > 0 ? "vitals.up" : "vitals.down");
            int amount = Mathf.Abs(Mathf.RoundToInt(delta));
            if (amount == 0) amount = 1; // clamped fraction: never "up 0"
            string factor = delta < 0 && _regainSeen
                && label.ToUpperInvariant().Contains(
                    Lex.T("vitals.stress").ToUpperInvariant())
                ? Lex.T("push.factor-six") : null;
            _deltas.Add(label + " " + sign + " " + amount
                + (factor != null ? ", " + factor : "")
                + ", " + Lex.T("outcome.now") + " " + nowFormatted + ".");
        }

        /// <summary>A cost-resource write that renders as NO visible change —
        /// both sides clamped to the same bound (sync pass MED-3). Still the
        /// cost signal: the compose gate opens, nothing composes.</summary>
        public static void NoteNullDelta(string label)
        {
            if (!_costPending) return;
            if (label == Lex.T("vitals.stress") || label == Lex.T("vitals.glitch"))
            {
                _costPending = false;
                Plugin.Log.LogInfo("[Push] cost observed as clamped no-change (" + label + ")");
            }
        }

        public static void Tick()
        {
            WatchDisarm();
            if (_fire == null) return;
            _fire.Ticks++;
            bool ready = _fire.Ticks >= 2 && DiceFlow.AllDiceSettled();
            if (!ready && Time.unscaledTime >= _fire.Deadline)
            {
                Plugin.Log.LogWarning("[Push] fire settle backstop hit — dice never rested (capture)");
                ready = true;
            }
            if (!ready) return;
            // The window holds for the cost delta (log finding 2026-08-02:
            // fire #1 composed without its stress clause) plus one post-settle
            // beat (sync pass MED-8) — the deadline stays the loud backstop.
            if (_costPending && Time.unscaledTime < _fire.Deadline) return;
            if (_fire.GraceTicks++ < 3 && Time.unscaledTime < _fire.Deadline) return;
            if (_costPending)
                Plugin.Log.LogWarning("[Push] composed without the cost delta — deadline hit (capture)");
            _costPending = false;
            var fire = _fire;
            _fire = null;
            Compose(fire);
        }

        /// <summary>The 4s native window lapsing (or any exit that is not the
        /// fire path) speaks a disarm — a dead second press must never be the
        /// first sign the window closed.</summary>
        private static void WatchDisarm()
        {
            if (!_armWatch) return;
            var fsm = SystemFsm();
            if (fsm == null)
            {
                // Scene teardown mid-arm is not a disarm (sync pass LOW-13).
                _armWatch = false;
                _modArmed = false;
                Plugin.Log.LogInfo("[Push] Push System gone mid-arm — watch dropped");
                return;
            }
            string state = fsm.ActiveStateName ?? "";
            if (ConfirmStates.Contains(state)) return;
            if (state.StartsWith("Gamepad")) return; // transit in
            _armWatch = false;
            if (_fire != null || state == ExecReboot || state == ExecRally
                || state == ExecFocus || state == "Set Stress"
                || state == "Check Stress" || state == "Check Usage" || state == "USED")
                return; // fired — the fire read owns the floor
            // Disarm speech only for arms WE made (sync pass MED-5: native
            // hover flap would chatter) — a native lapse just logs. Own
            // source so a modal pen holds the box AND this line, in order
            // (MED-10: same-source pen replaced the unheard box).
            bool ours = _modArmed;
            _modArmed = false;
            if (!ours)
            {
                Plugin.Log.LogInfo("[Push] native confirm stage lapsed — silent");
                return;
            }
            SpeechService.Say(Lex.T("push.disarmed"), Priority.Queued, "push-disarm");
        }

        /// <summary>The fire utterance (owner-ruled composition): "Push." +
        /// per-die clauses with the drawn faces + the handed stress movements
        /// in the delta grammar — ONE utterance (modal-pen survival).</summary>
        private static void Compose(Fire fire)
        {
            var sb = new System.Text.StringBuilder(Lex.T("push.fired"));
            if (fire.Class == ExecReboot)
            {
                foreach (int n in Sorted(_rerolled))
                    Append(sb, DieClause(n, "push.rerolled"));
                if (_rerolled.Count == 0)
                    Plugin.Log.LogWarning("[Push] REBOOT fired but no die Roll observed (capture)");
            }
            else if (fire.Class == ExecFocus)
            {
                // Stale-marker filter (sync pass MED-7): only dice newly
                // focused by THIS fire read as its work.
                if (fire.FocusedBefore != null)
                    _focused.RemoveWhere(n => n >= 1 && n <= 5 && fire.FocusedBefore[n - 1]);
                foreach (int n in Sorted(_focused))
                    Append(sb, DieClause(n, "push.focused"));
                if (_focused.Count == 0)
                {
                    // Glitched-lowest edge (D20): stress spent, no boost. The
                    // glitched die is the only value-9 candidate.
                    int glitched = GlitchedPlayerDie();
                    if (glitched > 0)
                        Append(sb, Lex.T("dice.die") + " " + glitched + " "
                            + Lex.T("push.glitched-noboost"));
                    else
                        Plugin.Log.LogWarning("[Push] FOCUS fired but no focus/glitch observed (capture)");
                }
            }
            else if (fire.Class == ExecRally && fire.CrewBefore != null)
            {
                bool any = false;
                for (int m = 1; m <= 2; m++)
                {
                    for (int s = 1; s <= 2; s++)
                    {
                        float before = fire.CrewBefore[(m - 1) * 2 + (s - 1)];
                        float after = CrewDieValue(m, s);
                        if (after <= 0f || after == before) continue;
                        any = true;
                        string name = CrewPanel.NameOf(m) ?? Lex.T("dice.crew") + " " + m;
                        Append(sb, name + " " + Lex.T("dice.die-lower") + " " + s + " "
                            + Lex.T("push.boosted") + ", " + Lex.T("outcome.now") + " "
                            + after.ToString("0"));
                    }
                }
                if (!any)
                    Plugin.Log.LogInfo("[Push] RALLY fired with no crew face change (clamp?) — deltas only");
            }
            foreach (var line in _deltas)
                Append(sb, line);
            _deltas.Clear();
            SpeechService.Say(sb.ToString(), Priority.Queued, "push");
        }

        /// <summary>"Die 3 rerolled, now 5." — face from the die's own drawn
        /// text (the render lane the dice reader already owns).</summary>
        private static string DieClause(int n, string verbKey)
        {
            string face = DiceFlow.FaceOf(n);
            var sb = new System.Text.StringBuilder(Lex.T("dice.die"));
            sb.Append(' ').Append(n).Append(' ').Append(Lex.T(verbKey));
            if (face != null)
                sb.Append(", ").Append(Lex.T("outcome.now")).Append(' ').Append(face);
            return sb.ToString();
        }

        private static List<int> Sorted(HashSet<int> set)
        {
            var list = new List<int>(set);
            list.Sort();
            return list;
        }

        private static int GlitchedPlayerDie()
        {
            for (int n = 1; n <= 5; n++)
            {
                var go = GameObject.Find("Letterbox Canvas/Top UI/Dice UI/Dice Slot " + n + "/Die");
                var fsm = go != null ? go.GetComponent<PlayMakerFSM>() : null;
                var v = fsm != null ? fsm.FsmVariables.GetFsmFloat("DiceValue") : null;
                if (v != null && v.Value == 9f) return n;
            }
            return 0;
        }

        private static float CrewDieValue(int member, int slot)
        {
            var go = GameObject.Find("Letterbox Canvas/Crew UI/Crew Member " + member
                + "/Display/Crew Dice/Crew - Dice Slot " + slot + "/Die");
            var fsm = go != null ? go.GetComponent<PlayMakerFSM>() : null;
            var v = fsm != null ? fsm.FsmVariables.GetFsmFloat("DiceValue") : null;
            return v != null ? v.Value : 0f;
        }

        private static PlayMakerFSM _systemFsm;

        private static PlayMakerFSM SystemFsm()
        {
            if (_systemFsm == null || _systemFsm.gameObject == null)
                _systemFsm = GameQueries.FindFsm("Push System", "Top UI");
            return _systemFsm;
        }
    }
}
