using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

/// <summary>
/// By default, a bot won't heal its limbs if it's below 5%, effectively not healing a surgically fixed limb.
/// </summary>
public class MinimumHealthPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(BotFirstAid), nameof(BotFirstAid.min_percent));
    }

    [PatchPrefix]
    public static bool PatchPrefix(BotFirstAid __instance, ref float __result)
    {
        __result = 0f;
        return false;
    }
}
