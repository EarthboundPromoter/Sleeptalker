using PixelCrushers.DialogueSystem;
using UnityEngine;

using Sleeptalker.Scaffold;

namespace Sleeptalker.Middleware
{
    /// <summary>
    /// Read-only adapter over the Dialogue System Lua database — the game's single
    /// authoritative store for persistent player state. CS2 re-key of the CS1
    /// LuaStore, from decode D15 (docs/decodes/D15-lua-allowlist.md, 2026-07-31).
    ///
    /// RENDER-PAIRING ALLOWLIST (binding, CS1 W1 invariant): this class exposes ONLY
    /// variables with a documented render pairing — somewhere the game shows the value
    /// to the player. Each getter cites its pairing (level2 pid citations are in the
    /// D15 doc). Everything else in the ~2,600-hit Player_* family is hidden story
    /// state and is never spoken; there is deliberately no generic Get(string) here.
    ///
    /// Corpus-derived and PENDING LIVE VALIDATION: static payloads don't separate
    /// read from write, so the pairings below get a live spot-check on the next ride
    /// (D15 open question 7) — build-plan offline-first amendment.
    ///
    /// Clock-tier variables (gate behavior, never spoken) live in <see cref="LuaClocks"/>.
    /// </summary>
    internal static class LuaStore
    {
        // ---------- Player stats ----------

        /// <summary>Render pairing: HUD energy bar — Top UI/Energy UI/Energy Bar/
        /// Energy Bar System polls this to drive the rendered boxes (D15).</summary>
        public static int? Energy() => Num("Player_Energy");

        /// <summary>Render pairing: HUD stress meter — Top UI/Stress Systems variants,
        /// plus every Dice UI Dice Slot Die FSM (D15). Replaces CS1's Player_Condition
        /// (absent in CS2 — the condition model is gone).</summary>
        public static int? Stress() => Num("Player_Stress");

        /// <summary>Render pairing: the cryo amount rendered at Bottom UI/Inventory/
        /// ITEMS Inventory UI/Cryo Slot /Amount — currency key unchanged from CS1 (D15).</summary>
        public static int? Bits() => Num("Player_Bits");

        /// <summary>Render pairing: ITEMS Inventory UI/Fuel Slot/Amount, the rig Engine
        /// Display fuel element, and the Map Screen stowage vector (D15). New in CS2.</summary>
        public static int? Fuel() => Num("Player_Fuel");

        /// <summary>Render pairing: ITEMS Inventory UI/Supplies Slot/Amount, the Energy
        /// Bar System's contract-mode Supplied count, and Map Screen stowage (D15).</summary>
        public static int? Supplies() => Num("Player_Supplies");

        /// <summary>Render pairing: Top UI/Glitch Markers dial (/6) and each Dice Slot
        /// Die render (D15).</summary>
        public static int? Glitch() => Num("Player_Glitch");

        /// <summary>Render pairing: Top UI/Glitch Markers/Permanent Glitch Clock (/6)
        /// and each Dice Slot Die render (D15).</summary>
        public static int? PermaGlitch() => Num("Player_PermaGlitch");

        /// <summary>Render pairing: Character Screen/Upgrade Tracker readout plus the
        /// SKILL List upgrade-stack and class PUSH buy FSMs (D15).</summary>
        public static int? UpgradePoints() => Num("Player_UpgradePoints");

        /// <summary>Render pairing: Character UI Button/Class Name, Character Screen
        /// SKILL List/Class Name, and Character Portrait (D15).</summary>
        public static string PlayerClass() => Str("Player_Class");

        // ---------- Crew (two slots exactly — D15 structural finding) ----------

        /// <summary>Render pairing CANDIDATE (D15): crew stress feeds Top UI/Push System
        /// crew-dice availability; the contract-screen stress bars bind runtime-built
        /// keys. Confirm the bar binding live before this becomes announce material —
        /// the Vitals crew channel reads the bar FSM's own vars, not this.</summary>
        public static int? CrewStress(int slot) => Num("Crew" + slot + "_Stress");

        /// <summary>Render pairing CANDIDATE (D15): crew name strings rendered on
        /// action-card crew skill rows (~289 Action Controllers).</summary>
        public static string CrewMemberName(int slot) => Str("CrewMember" + slot);

        // ---------- Skills ----------

        public enum Skill { Endure, Engage, Engineer, Interface, Intuit }

        // Enum-order-bound; identical keys to CS1 (the Com.CitizenSleeper.Skill enum is
        // literally shared between the games). This array is the GameVocab re-key seam —
        // it migrates there when the CS2 vocabulary module is born.
        private static readonly string[] SkillVars =
            { "ENDURE", "ENGAGE", "ENGINEER", "INTERFACE", "INTUIT" };

        /// <summary>The skill's value — the number whose bucket the character screen
        /// renders. Render pairing: SKILL List/Individual Skills/&lt;SKILL&gt;/Skill
        /// Upgrade Stack, GetVariable + FloatSwitch idiom verbatim from CS1, plus the
        /// Top UI Skill Icons row (D15).</summary>
        public static int? SkillValue(Skill skill) => Num(SkillVars[(int)skill]);

        /// <summary>The rendered modifier bucket (-1/0/+1/+2) for a skill, reproducing
        /// the game's own FloatSwitch mapping (lessThan [0,1,2,3] → [-1,0,+1,+2]) —
        /// carried from CS1; the Skill Upgrade Stack FSMs run the same idiom in CS2
        /// (D15). Null when unavailable — or ≥3, where the game's switch fires no event
        /// (mirrored, logged).</summary>
        public static string SkillModifier(Skill skill)
        {
            string var = SkillVars[(int)skill];
            try
            {
                var r = DialogueLua.GetVariable(var);
                if (!r.hasReturnValue || !r.isNumber) return null;
                float v = r.asFloat;
                if (v < 0f) return "-1";
                if (v < 1f) return "0";
                if (v < 2f) return "+1";
                if (v < 3f) return "+2";
                Plugin.Log.LogWarning("[Substrate] " + var + "=" + v
                    + " is outside the game's FloatSwitch range; no bucket spoken.");
                return null;
            }
            catch (System.Exception e) { LogFaultOnce(var, e); return null; }
        }

        /// <summary>Bucket lookup by the rendered skill word (action cards render the
        /// exact Lua variable names, CS1 idiom); null for a non-skill word.</summary>
        public static string SkillModifierForWord(string skillWord)
        {
            int idx = System.Array.IndexOf(SkillVars, skillWord);
            return idx >= 0 ? SkillModifier((Skill)idx) : null;
        }

        // CS1's PerkCount is NOT here: CS2 re-purposes the *_PERKS keys as mechanic
        // gates with no rendered rung (D15) — they live in LuaClocks now.
        // CS1's Condition/DrivePoints/CycleNumber getters retired: Player_Condition and
        // Player_DrivePoints are absent in CS2; Cycle has no proven render pairing yet
        // (gate-only in LuaClocks until a ride finds a rendered cycle number).

        // ---------- Plumbing ----------

        private static bool _faultLogged;

        /// <summary>Presence-aware numeric read: null means "not available" (no database
        /// yet, or the variable is unset) — callers must distinguish that from a real 0.</summary>
        public static int? Num(string name)
        {
            try
            {
                var r = DialogueLua.GetVariable(name);
                if (!r.hasReturnValue || !r.isNumber) return null;
                return Mathf.RoundToInt(r.asFloat);
            }
            catch (System.Exception e) { LogFaultOnce(name, e); return null; }
        }

        /// <summary>Unrounded variant for mirrors of the game's own float
        /// compares (sync pass 2026-08-03: travel deducts an unrounded float —
        /// a rounded read diverges at half-boundaries).</summary>
        public static float? NumF(string name)
        {
            try
            {
                var r = DialogueLua.GetVariable(name);
                if (!r.hasReturnValue || !r.isNumber) return null;
                return r.asFloat;
            }
            catch (System.Exception e) { LogFaultOnce(name, e); return null; }
        }

        private static string Str(string name)
        {
            try
            {
                var r = DialogueLua.GetVariable(name);
                if (!r.hasReturnValue || !r.isString) return null;
                string s = r.asString;
                return string.IsNullOrEmpty(s) || s == "nil" ? null : s;
            }
            catch (System.Exception e) { LogFaultOnce(name, e); return null; }
        }

        private static void LogFaultOnce(string name, System.Exception e)
        {
            if (_faultLogged) return;
            _faultLogged = true;
            Plugin.Log.LogWarning("[Substrate] Lua read failed (" + name + "): " + e.Message);
        }
    }

    /// <summary>
    /// Clock-tier Lua reads: these GATE behavior, they are never spoken (invariant 1 —
    /// signals are clocks, never content). Separate type so a speakable value can't be
    /// pulled from here by accident. CS2 set per decode D15.
    /// </summary>
    internal static class LuaClocks
    {
        /// <summary>Intro gating flag — survives from CS1 (D15: DEBUG Intro Skipper +
        /// RIG MAIN Drive Triggers readers).</summary>
        public static bool IntroComplete()
        {
            try
            {
                var r = DialogueLua.GetVariable("IntroComplete");
                return r.hasReturnValue && r.isNumber ? r.asFloat >= 1f
                     : r.hasReturnValue && r.isBool && r.asBool;
            }
            catch { return false; }
        }

        /// <summary>Reroll-spent flag — survives from CS1 in the same role (D15:
        /// REROLL DICE + Push System readers). Gate-only: picks refusal wording,
        /// never spoken as a value.</summary>
        public static bool RerollUsed()
        {
            try
            {
                var r = DialogueLua.GetVariable("REROLL");
                return r.hasReturnValue && r.isNumber && r.asFloat >= 1f;
            }
            catch { return false; }
        }

        /// <summary>The live cycle number — DEMOTED from CS1's speakable tier (D15):
        /// single writer Cycle Controller confirmed, but CS2's save-slot labels are
        /// C#-driven and no static render consumer exists. Promotes back to LuaStore
        /// if a ride finds a rendered cycle number.</summary>
        public static int? CycleNumber() => LuaStore.Num("Cycle");

        /// <summary>Purchased perk rungs — gate-tier in CS2 (D15): the *_PERKS keys
        /// gate buy prices, PREDICTIVE outcome blocks, reroll and perk managers; no UI
        /// renders rung state. Used for refusal reasoning only.</summary>
        public static int? PerkCount(LuaStore.Skill skill)
            => LuaStore.Num(SkillKey(skill) + "_PERKS");

        /// <summary>Skill-break flag (new CS2 family, D15): read beside *_PERKS by the
        /// Perks Manager / Cycle Controller as perk-disable gates.</summary>
        public static bool SkillBroken(LuaStore.Skill skill)
        {
            var v = LuaStore.Num(SkillKey(skill) + "_BROKEN");
            return v.HasValue && v.Value >= 1;
        }

        /// <summary>Crew-break flag (new CS2 family, D15): crew-dice disable gates in
        /// the Push System. The rendered break experience speaks for itself.</summary>
        public static bool CrewBroken(int slot)
        {
            var v = LuaStore.Num("Crew" + slot + "_BROKEN");
            return v.HasValue && v.Value >= 1;
        }

        // CS1's BreakdownCycle retired: BREAKDOWN_CYCLE is absent in CS2 — breakdown
        // scheduling is replaced by the stress-break families above (D15).

        private static string SkillKey(LuaStore.Skill skill)
        {
            switch (skill)
            {
                case LuaStore.Skill.Endure: return "ENDURE";
                case LuaStore.Skill.Engage: return "ENGAGE";
                case LuaStore.Skill.Engineer: return "ENGINEER";
                case LuaStore.Skill.Interface: return "INTERFACE";
                default: return "INTUIT";
            }
        }
    }
}
