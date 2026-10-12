using System;
using System.Reflection;
using BotHealingFixes.Utils;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches.Debug;

[DebugPatch]
public class RegisterTakeWeaponPatch : ModulePatch
{
    private static bool _shouldRun;

    private static readonly MethodInfo _takeMainWeaponInfo = AccessTools.Method(
        typeof(BotWeaponSelector),
        nameof(BotWeaponSelector.TakeMainWeapon)
    );

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(AITaskManager),
            nameof(AITaskManager.RegisterDelayedTask),
            [typeof(BotOwner), typeof(float), typeof(Action)]
        );
    }

    [PatchPostfix]
    public static void PatchPostfix(AITaskManager __instance, BotOwner bot, float delay, Action task)
    {
        if (!_shouldRun)
        {
            return;
        }
        if (task.Method == _takeMainWeaponInfo)
        {
            Logger.LogDebug($"RegisterDelayedTask: [{bot.Name()}] Registered TakeMainWeapon\n\n{Environment.StackTrace}");
        }
    }
}
