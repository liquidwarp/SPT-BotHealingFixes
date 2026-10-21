using System.Collections.Generic;
using System.Reflection;
using BotHealingFixes.Utils;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

/// <summary>
/// This fixes getting medical items for the bot:
///   1. When bleeding, use an item that can afford to treat the bleed it will be applied to, including tourniquets and bandages,
///      preferring one that treats a heavy bleed. A injector that stops bleeds is the last resort.
///   2. Otherwise use the first medkit with resource left, then a healing injector as a last resort, or nothing rather than
///      keeping a used up item
///   3. Avoid LINQ
/// </summary>
public class RefreshMedsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotFirstAid).GetMethod(nameof(BotFirstAid.RefreshMeds));
    }

    [PatchPrefix]
    protected static bool Prefix(BotFirstAid __instance)
    {
        if (!__instance._owner.Settings.FileSettings.Mind.CAN_USE_MEDS)
        {
            return false;
        }

        var medsList = __instance.GetAvailableMeds(__instance._shallUseInSafe);
        __instance.CurUsingMeds =
            __instance._health.FindBleedMeds(medsList)
            ?? FindHealMeds(medsList)
            ?? StimUtils.FindHealStim(__instance._owner.GetPlayer.ActiveHealthController, medsList);
        return false;
    }

    private static Meds FindHealMeds(List<Meds> medsList)
    {
        foreach (var meds in medsList)
        {
            if (meds is MedKit medKit && medKit.MedKitComponent?.HpResource > 0f)
            {
                return medKit;
            }
        }
        return null;
    }
}
