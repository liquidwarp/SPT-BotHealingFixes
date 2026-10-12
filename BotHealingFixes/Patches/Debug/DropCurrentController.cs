using System;
using System.Reflection;
using BotHealingFixes.Utils;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches.Debug;

[DebugPatch]
public class DropCurrentController : ModulePatch
{
    private static bool _shouldRun;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Player), nameof(Player.DropCurrentController));
    }

    [PatchPostfix]
    public static void PatchPostfix(Player __instance, Item nextControllerItem)
    {
        if (!_shouldRun)
        {
            return;
        }
        L.Debug(
            $"DropCurrentController: [{__instance.Profile.GetCorrectedNickname()}] Dropping current controller, next item {nextControllerItem.LocalizedName()}\n\n{Environment.StackTrace}"
        );
    }
}
