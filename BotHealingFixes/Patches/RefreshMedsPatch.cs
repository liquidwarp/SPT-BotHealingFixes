using System.Reflection;
using BotHealingFixes.Utils;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

/// <summary>
/// This fixes getting medical items for the bot:
///   1. Med item should be able to heal a light or heavy bleed based on its resource cost
///   2. Avoid LINQ
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

        var bleedType = __instance._health.GetBleedType(__instance._bodyPartToHeal ?? EBodyPart.Common);

        var medsList = __instance._medsList;
        medsList.Clear();
        __instance._owner.GetPlayer.InventoryController.GetAcceptableItemsNonAlloc(
            __instance._shallUseInSafe ? BotMedecine.secureSlots : BotMedecine.anySlots,
            medsList
        );
        foreach (var meds in medsList)
        {
            if (meds is not MedKit medKit)
            {
                continue;
            }

            switch (bleedType)
            {
                case MedUtils.EBleedType.Light:
                    if (
                        medKit.HealthEffectsComponent.Affects(EDamageEffectType.LightBleeding, out var cost)
                        && medKit.MedKitComponent?.HpResource > cost
                    )
                    {
                        __instance.CurUsingMeds = medKit;
                        return false;
                    }
                    continue;
                case MedUtils.EBleedType.Heavy:
                    if (
                        medKit.HealthEffectsComponent.Affects(EDamageEffectType.HeavyBleeding, out cost)
                        && medKit.MedKitComponent?.HpResource > cost
                    )
                    {
                        __instance.CurUsingMeds = medKit;
                        return false;
                    }
                    continue;
                case MedUtils.EBleedType.None:
                default:
                    if (medKit.MedKitComponent?.HpResource > 0f)
                    {
                        __instance.CurUsingMeds = medKit;
                        return false;
                    }
                    continue;
            }
        }
        return false;
    }
}
