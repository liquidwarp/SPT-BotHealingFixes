using System.Reflection;
using BotHealingFixes.Utils;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

public class MedsEventsActivatePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotMedecine), nameof(BotMedecine.Activate));
    }

    [PatchPostfix]
    protected static void Postfix(BotMedecine __instance)
    {
        MedsEventsHandler.Subscribe(__instance);
    }
}

public class MedsEventsDisposePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotMedecine), nameof(BotMedecine.Dispose));
    }

    [PatchPrefix]
    protected static void Prefix(BotMedecine __instance)
    {
        MedUtils.ForgetBot(__instance);
        MedsEventsHandler.Unsubscribe(__instance);
    }
}
