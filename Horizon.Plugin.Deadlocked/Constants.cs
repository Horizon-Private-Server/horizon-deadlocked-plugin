using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked
{
    public enum GadgetSlots
    {
        Wrench,
        Vipers,
        MagmaCannon,
        Arbiter,
        Fusion,
        MineLauncher,
        B6,
        Flail,
        Holoshields,
    }

    public enum Gadgets
    {
        None = 0,
        Wrench,
        Vipers,
        MagmaCannon,
        Arbiter,
        Fusion,
        MineLauncher,
        B6,
        Holoshields,
        Miniturret,
        Harbinger,
        Grindrail,
        EMP,
        HackerRay,
        MpGrapplingHook,
        Flail,
        ShieldLink,
        MpChargeBoots,
        MpMagnetBoots,
        MpGrindBoots
    }

    public enum OmegaMods
    {
        None = 0,
        Napalm,
        TimeBomb,
        Freeze,
        MiniBomb,
        Morph,
        Brainwash,
        Acid,
        Shock
    }

    public enum AlphaMods
    {
        None = 0,
        Speed,
        Ammo,
        Aiming,
        Impact,
        Area,
        Xp,
        Jackpot,
        Nanoleech
    }

    public static class Constants
    {
        public static readonly string[] Teams = new string[]
        {
            "Blue",
            "Red",
            "Green",
            "Orange",
            "Yellow",
            "Purple",
            "Aqua",
            "Pink",
            "Olive",
            "Maroon"
        };
    }
}
