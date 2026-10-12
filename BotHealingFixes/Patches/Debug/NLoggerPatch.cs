using System.Reflection;
using BotHealingFixes.Utils;
using HarmonyLib;
using NLog;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches.Debug;

[DebugPatch]
public class NLoggerPatch : ModulePatch
{
    private static bool _shouldRun;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(AbstractLogger),
            nameof(AbstractLogger.Log),
            [typeof(string), typeof(string), typeof(LogLevel), typeof(object[])]
        );
    }

    [PatchPostfix]
    public static void PatchPostfix(AbstractLogger __instance, string nlogFormat, string unityFormat, LogLevel logLevel, object[] args)
    {
        if (!_shouldRun)
        {
            return;
        }
        if (__instance.NLoggerName != "player")
        {
            return;
        }
        if (args is null || args.Length == 0)
        {
            L.Debug($"[NLog : {logLevel.Name} : {__instance.NLoggerName}] {unityFormat}");
            return;
        }

        L.Debug($"[NLog : {logLevel.Name} : {__instance.NLoggerName}] {string.Format(unityFormat, args)}");
    }
}
