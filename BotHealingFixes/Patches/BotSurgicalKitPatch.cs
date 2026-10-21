using System;
using System.Reflection;
using BotHealingFixes.Utils;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

public class BotSurgicalKitPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotSurgicalKit), nameof(BotSurgicalKit.ApplyToCurrentPart));
    }

    [PatchPrefix]
    public static bool PatchPrefix(BotSurgicalKit __instance, Action callbackEndUse)
    {
        if (
            !__instance._bodyPartToHeal.HasValue
            || !__instance.HaveSmth2Use
            || __instance._owner.WeaponManager.Reload.Reloading
            || !__instance._owner.GetPlayer.HealthController.CanTreat(
                __instance.CurUsingMeds,
                EDamageEffectType.DestroyedPart,
                __instance._bodyPartToHeal.Value
            )
        )
        {
            return false;
        }

        _ = __instance.ApplyMedicineAsync(TimeSpan.FromSeconds(60D), callbackEndUse);
        return false;
    }
}
