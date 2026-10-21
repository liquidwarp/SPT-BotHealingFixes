using System.Reflection;
using BotHealingFixes.Utils;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

/// This fixes getting a surgical item for the bot:
///   1. The item must be able to pay the surgery cost, not just have a surgery effect. Common with mods that give medkits surgery.
///   2. Prefer dedicated surgical kits.
///   3. Pick nothing rather than keeping a used up item.
public class SurgicalRefreshMedsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotSurgicalKit).GetMethod(nameof(BotSurgicalKit.RefreshMeds));
    }

    [PatchPrefix]
    protected static bool Prefix(BotSurgicalKit __instance)
    {
        if (!__instance._owner.Settings.FileSettings.Mind.CAN_USE_MEDS)
        {
            return false;
        }

        __instance.CurUsingMeds = MedUtils.FindSurgeryMeds(__instance.GetAvailableMeds(__instance._shallUseInSafe));
        return false;
    }
}
