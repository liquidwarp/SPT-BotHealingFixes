using System.Reflection;
using BotHealingFixes.Utils;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

public class CanHealDamageEffectTypePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.CanHealDamageEffectType));
    }

    [PatchPrefix]
    protected static bool Prefix(Meds med, EDamageEffectType damageEffectType, ref bool __result)
    {
        __result = med != null && MedUtils.CanTreat(med, damageEffectType);
        return false;
    }
}

public class FindDamagedPartPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.FindDamagedPart));
    }

    [PatchPrefix]
    protected static void Prefix(BotFirstAid __instance)
    {
        if (__instance.CurUsingMeds == null)
        {
            __instance._isBleedingHeavy = false;
            __instance._isBleedingLight = false;
        }
    }
}
