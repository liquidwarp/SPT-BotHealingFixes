using System;
using System.Reflection;
using BotHealingFixes.Utils;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

public class StimulatorsFindMedsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotStimulators), nameof(BotStimulators.FindMeds));
    }

    [PatchPrefix]
    protected static bool Prefix(BotStimulators __instance)
    {
        if (!__instance._owner.Settings.FileSettings.Mind.CAN_USE_MEDS)
        {
            return false;
        }

        __instance._stimulator = StimUtils.FindStimulator(__instance, __instance.GetAvailableMeds(__instance._shallUseInSafe));
        __instance.HaveSmt = __instance._stimulator != null;
        return false;
    }
}

public class StimulatorsTryApplyPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotStimulators), nameof(BotStimulators.TryApply));
    }

    // ORBIT compat.
    [PatchPrefix]
    [HarmonyPriority(Priority.Low)]
    protected static bool Prefix(BotStimulators __instance, bool noCheckDelay, Action<bool> callback, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return false;
        }

        if (__instance._owner.WeaponManager.Reload.Reloading || __instance.Using || (!noCheckDelay && !__instance.CanUseNow()))
        {
            callback?.Invoke(false);
            return false;
        }

        __instance.FindMeds();
        if (__instance._stimulator == null)
        {
            callback?.Invoke(false);
            return false;
        }

        __instance.ApplyMedicine(
            __instance._stimulator,
            EBodyPart.Chest,
            30f,
            null,
            callback
        );
        return false;
    }
}
