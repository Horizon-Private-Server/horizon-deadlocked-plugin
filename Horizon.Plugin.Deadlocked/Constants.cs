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

        public static Gadgets ToGadget(this GadgetSlots slot)
        {
            switch (slot)
            {
                case GadgetSlots.Wrench: return Gadgets.Wrench;
                case GadgetSlots.Vipers: return Gadgets.Vipers;
                case GadgetSlots.MagmaCannon: return Gadgets.MagmaCannon;
                case GadgetSlots.Arbiter: return Gadgets.Arbiter;
                case GadgetSlots.Fusion: return Gadgets.Fusion;
                case GadgetSlots.MineLauncher: return Gadgets.MineLauncher;
                case GadgetSlots.B6: return Gadgets.B6;
                case GadgetSlots.Holoshields: return Gadgets.Holoshields;
                case GadgetSlots.Flail: return Gadgets.Flail;
                default: return Gadgets.None;
            }
        }

        public static GadgetSlots? ToGadgetSlot(this Gadgets gadget)
        {
            switch (gadget)
            {
                case Gadgets.Wrench: return GadgetSlots.Wrench;
                case Gadgets.Vipers: return GadgetSlots.Vipers;
                case Gadgets.MagmaCannon: return GadgetSlots.MagmaCannon;
                case Gadgets.Arbiter: return GadgetSlots.Arbiter;
                case Gadgets.Fusion: return GadgetSlots.Fusion;
                case Gadgets.MineLauncher: return GadgetSlots.MineLauncher;
                case Gadgets.B6: return GadgetSlots.B6;
                case Gadgets.Holoshields: return GadgetSlots.Holoshields;
                case Gadgets.Flail: return GadgetSlots.Flail;
                default: return null;
            }
        }
    }
}
