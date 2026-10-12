using System;
using System.Reflection;
using BotHealingFixes.Utils;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

public class BotMedKitPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.ApplyToSelf));
    }

    [PatchPrefix]
    public static bool PatchPrefix(BotFirstAid __instance, Action callback)
    {
        _ = __instance.ApplyMedicineAsync(TimeSpan.FromSeconds(30D), callback);
        return false;
    }
}
