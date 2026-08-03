using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Priority = Sleeptalker.Scaffold.Priority;

using Sleeptalker.Scaffold;
using Sleeptalker.Middleware;

namespace Sleeptalker.Sleeper2
{
    /// <summary>Crew member panel readers + watchers (decode D10; owner ruling
    /// batch 2026-08-02, the Checkpoint C crew session).
    ///
    /// The panels render ONLY in contract scenes (the member panel FSM gates on
    /// Current Location Scene containing CRT — byte-decoded), so every reader
    /// here is null when nothing is drawn; consumers treat null as
    /// nonexistence.
    ///
    /// Rulings of record:
    ///  - Crew dice and stress attribute by the member's RENDERED panel name;
    ///    "Crew N" is the logged fallback.
    ///  - A stressed-out member speaks "NAME. Disabled." at the member level,
    ///    keyed to the rendered KO state; their dice read plain "spent".
    ///  - Skill ratings ARE spoken: the chips render as art with no text
    ///    (D10), so the wording uses the panel FSM's OWN skill strings and
    ///    table rating — the sanctioned practical lane for art-only render —
    ///    with the chip bands reproduced with provenance (rating &lt;2 draws 0,
    ///    ==2 draws +1, &gt;2 draws +2; D10 (a) member panel FSM 30596).
    ///  - Barks speak when they render, name-prefixed, queued flavor.</summary>
    internal static class CrewPanel
    {
        public static void Init()
        {
            // The KO moment: the crew stress bar family's Full Stress entry
            // (render side: Display animator Broken + slot Deactivated
            // overlays + KO sting). Spoken at the member level per ruling.
            FsmSignals.Subscribe(null, "Full Stress", (fsm, s) => OnFullStress(fsm));

            // Barks: rendered crew speech (Bark System states Bark / Bark 2..6,
            // D10 recipe 4) — spoken verbatim at the signal, name-prefixed.
            FsmSignals.Subscribe(null, "Bark", (fsm, s) => OnBark(fsm));
            for (int i = 2; i <= 6; i++)
                FsmSignals.Subscribe(null, "Bark " + i, (fsm, s) => OnBark(fsm));
        }

        // ---------- Panel resolution ----------

        private static Transform MemberRoot(int member)
        {
            var go = GameObject.Find("Letterbox Canvas/Crew UI/Crew Member " + member);
            return go != null && go.activeInHierarchy ? go.transform : null;
        }

        /// <summary>The member's Display when it is actually drawn (contract
        /// scenes with this slot deployed). Null = no rendered panel.</summary>
        public static Transform PanelOf(int member)
        {
            var root = MemberRoot(member);
            var display = root != null ? root.Find("Display") : null;
            if (display == null || !display.gameObject.activeInHierarchy) return null;
            return Util.RenderedUp(display) ? display : null;
        }

        // Last rendered name per member (owner ruling 2026-08-02, the
        // "Crew 1/Crew 2" cycle-summary regression: the lookup died with the
        // contract scene's panels). The cache keeps attribution stable across
        // undrawn phases; it never CREATES speech — crew surfaces stay gated
        // on the rendered panel.
        private static readonly string[] _nameCache = new string[3];

        /// <summary>The member's rendered name (the panel Name TMP — the render
        /// Vitals' provisional "Crew N" graduates to), falling back to the
        /// last name this session rendered. Null only before first render.</summary>
        public static string NameOf(int member)
        {
            if (member < 1 || member > 2) return null;
            var display = PanelOf(member);
            if (display != null)
            {
                foreach (var t in display.GetComponentsInChildren<Transform>(false))
                {
                    if (t.name != "Name") continue;
                    var tmp = t.GetComponent<TMP_Text>();
                    if (tmp == null || !Util.RenderedUp(tmp.transform)) continue;
                    string text = SpeechService.Clean(tmp.text);
                    if (!string.IsNullOrEmpty(text))
                    {
                        _nameCache[member] = text;
                        return text;
                    }
                }
            }
            return _nameCache[member];
        }

        /// <summary>Member from any transform under a Crew Member subtree
        /// (bars, barks, dice); 0 = not crew-homed.</summary>
        public static int MemberOfTransform(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur.name.StartsWith("Crew Member "))
                    return (int)Util.LeadingInt(cur.name.Substring(12));
            return 0;
        }

        /// <summary>The rendered KO state: any Deactivated overlay drawn on the
        /// member's dice slots (D10 (e) — the broken-crew render).</summary>
        public static bool DisabledUp(int member)
        {
            var root = MemberRoot(member);
            if (root == null) return false;
            foreach (var t in root.GetComponentsInChildren<Transform>(false))
                if (t.name == "Deactivated" && Util.RenderedUp(t)) return true;
            return false;
        }

        // ---------- The composite member read (V-row cell; ruled order:
        // name, disabled, stress, skills, dice) ----------

        /// <summary>"SERAFIN." / "SERAFIN. Disabled." — the crew row's name
        /// cell (owner ruling 2026-08-02: crew are V-table rows).</summary>
        public static string NameStatusCell(int member)
        {
            if (PanelOf(member) == null) return null;
            string name = NameOf(member) ?? Lex.T("dice.crew") + " " + member;
            return DisabledUp(member)
                ? name + ". " + Lex.T("zone.disabled")
                : name + ".";
        }

        /// <summary>The skills cell, value wording (owner ruling 2026-08-02:
        /// skills always read as values).</summary>
        public static string SkillsCell(int member) => SkillsLine(member);

        /// <summary>The stress cell for the member's own V-table row (owner
        /// ruling 2026-08-02: crew stress reviews per member, not in the
        /// vitals row).</summary>
        public static string StressCell(int member)
        {
            var display = PanelOf(member);
            return display != null ? StressLine(display) : null;
        }

        public static string MemberLine(int member)
        {
            var display = PanelOf(member);
            if (display == null) return null;
            string name = NameOf(member);
            if (name == null)
            {
                name = Lex.T("dice.crew") + " " + member;
                LogOnce("[Crew] panel " + member + " drawn but no rendered Name — fallback spoken");
            }
            var sb = new System.Text.StringBuilder(name).Append('.');
            if (DisabledUp(member)) sb.Append(' ').Append(Lex.T("zone.disabled"));
            string stress = StressLine(display);
            if (stress != null) sb.Append(' ').Append(stress);
            string skills = SkillsLine(member);
            if (skills != null) sb.Append(' ').Append(skills);
            for (int slot = 1; slot <= 2; slot++)
            {
                string die = DiceFlow.CrewDieLine(member, slot, withName: false);
                if (die != null) sb.Append(' ').Append(die);
            }
            return sb.ToString();
        }

        /// <summary>"Stress 3 of 6." from the ACTIVE bar variant's own vars —
        /// the fill-only-bar practical lane, caps byte-true per variant
        /// (Regular 6 / Weak 4 / Resistant 8, D10).</summary>
        private static string StressLine(Transform display)
        {
            foreach (Transform child in display)
            {
                if (!child.name.Contains("Stress")) continue;
                if (!child.gameObject.activeInHierarchy) continue;
                var fsm = child.GetComponent<PlayMakerFSM>();
                if (fsm == null) continue;
                var stress = fsm.FsmVariables.GetFsmFloat("Stress");
                if (stress == null) continue;
                float max = Vitals.CrewMax(fsm);
                // Bounds ruling 2026-08-02: overflow is wrong in BOTH
                // directions — the game overshoots past cap to trigger the
                // disable ("Stress 7 of 6" heard live); the bar never draws
                // past its pips.
                float value = stress.Value < 0f ? 0f
                    : (max > 0f && stress.Value > max ? max : stress.Value);
                if (value != stress.Value)
                    LogOnce("[Crew] stress clamped: raw " + stress.Value
                        + " spoken as " + value + " (" + child.name + ")");
                return Lex.T("vitals.stress") + " " + value.ToString("0.#")
                    + (max > 0f ? " " + Lex.T("vitals.of") + " " + max.ToString("0") : "")
                    + ".";
            }
            return null;
        }

        /// <summary>"ENGINEER plus 1. INTERFACE 0." — skill identity and rating
        /// from the member panel FSM's own vars (the strings the game itself
        /// dispatches the icon render from); chip bands reproduced with
        /// provenance (art-only render — no text exists to read).</summary>
        private static string SkillsLine(int member)
        {
            var root = MemberRoot(member);
            if (root == null) return null;
            PlayMakerFSM panel = null;
            foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(false))
                if (fsm.FsmVariables.GetFsmString("Skill") != null) { panel = fsm; break; }
            if (panel == null)
            {
                LogOnce("[Crew] member " + member + ": no panel FSM carrying Skill — ratings silent");
                return null;
            }
            var sb = new System.Text.StringBuilder();
            AppendSkill(sb, panel, "Skill", "Skill Rating");
            AppendSkill(sb, panel, "Skill 2", "Skill 2 Rating");
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static void AppendSkill(System.Text.StringBuilder sb,
            PlayMakerFSM panel, string skillVar, string ratingVar)
        {
            var skill = panel.FsmVariables.GetFsmString(skillVar);
            if (skill == null || string.IsNullOrEmpty(skill.Value)
                || skill.Value == "none") return;
            float rating;
            var f = panel.FsmVariables.GetFsmFloat(ratingVar);
            if (f != null) rating = f.Value;
            else
            {
                var i = panel.FsmVariables.GetFsmInt(ratingVar);
                if (i == null)
                {
                    LogOnce("[Crew] rating var \"" + ratingVar + "\" missing — skill spoken bare");
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(skill.Value).Append('.');
                    return;
                }
                rating = i.Value;
            }
            // The chip bands (D10 (a), FSM 30596): <2 draws 0, ==2 draws +1,
            // >2 draws +2 — reproduced here because the drawn chip is art.
            string chip = rating < 1.5f ? "0"
                : rating < 2.5f ? Lex.T("glyph.plus") + " 1"
                : Lex.T("glyph.plus") + " 2";
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(skill.Value).Append(' ').Append(chip).Append('.');
        }

        // ---------- Watchers ----------

        private static void OnFullStress(PlayMakerFSM fsm)
        {
            if (fsm == null || !fsm.gameObject.name.Contains("Stress")) return;
            int member = MemberOfTransform(fsm.transform);
            if (member == 0) return;
            // Undrawn bars stay silent (the hub-suppression ruling — the wake
            // summary and the next contract panel carry the truth).
            if (!Util.RenderedUp(fsm.transform)) return;
            string name = NameOf(member) ?? Lex.T("dice.crew") + " " + member;
            SpeechService.Say(name + ". " + Lex.T("zone.disabled"),
                Priority.Queued, "crew");
        }

        private static void OnBark(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject.name != "Bark System") return;
            int member = MemberOfTransform(fsm.transform);
            string text = null;
            foreach (var tmp in fsm.GetComponentsInChildren<TMP_Text>(false))
            {
                // Signal-time capture: the bark TMP is set in the state before
                // the fade animator runs — alpha may still be climbing.
                string t = SpeechService.Clean(tmp.text);
                if (!string.IsNullOrEmpty(t)) { text = t; break; }
            }
            if (text == null)
            {
                LogOnce("[Crew] bark signal with no text — capture");
                return;
            }
            string name = member > 0 ? NameOf(member) : null;
            SpeechService.Say(name != null ? name + ": " + text : text,
                Priority.Queued, "crew");
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void LogOnce(string line)
        {
            if (Logged.Add(line)) Plugin.Log.LogWarning(line);
        }
    }
}
