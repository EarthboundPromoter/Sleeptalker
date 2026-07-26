using System;
using UnityEngine;

namespace Sleeptalker.Scaffold
{
    /// <summary>Tier seam (stage 1, 2026-07-25): the scaffold and middleware speech
    /// surfaces (Navigator, FocusPatch) describe UI elements through this injected
    /// delegate; Plugin registers the game layer's Describe.Element at init. This
    /// delegate IS the element-description channel — focus announcements, dead-end
    /// repeats, disabled-activation refusals — so the scaffold never references the
    /// game tier. Default: the bare object name, so a missed registration is
    /// audible ("Continue Button" instead of "Continue"), never silent.</summary>
    internal static class ElementDescriber
    {
        public static Func<GameObject, bool, string> Element =
            (go, detailed) => go != null ? go.name : null;
    }

    /// <summary>Tier seam, same contract as ElementDescriber: the scaffold Navigator
    /// signals user-initiated navigation to the focus-policy layer (FocusPatch)
    /// through these delegates; Plugin wires them at init. No-op defaults — a
    /// missed registration degrades to game-driven announcement behavior, which is
    /// audible (deferred/queued instead of immediate), never silent.</summary>
    internal static class NavSignals
    {
        /// <summary>Fired before Navigator moves or sets selection.</summary>
        public static Action UserNavigated = () => { };

        /// <summary>Fired before user-initiated selection so it always announces.</summary>
        public static Action<GameObject> ClearCooldown = go => { };
    }
}
