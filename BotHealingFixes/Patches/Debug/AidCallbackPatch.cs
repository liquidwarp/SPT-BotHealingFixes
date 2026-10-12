using System.Reflection;
using BotHealingFixes.Utils;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches.Debug;

[DebugPatch]
public class AidCallbackPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotMedecine), nameof(BotMedecine.AidCallback));
    }

    [PatchPrefix]
    public static void PatchPrefix(BotMedecine __instance, bool obj)
    {
        L.Debug($"{__instance.Name()} Received callback {obj}");
    }
}
