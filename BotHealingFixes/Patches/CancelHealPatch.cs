using System.Reflection;
using BotHealingFixes.Utils;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

// The application owns cancellation and weapon restoration; changing an operation's State alone skips its cleanup.
public class CancelHealPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotAbstractMeds).GetMethod(nameof(BotAbstractMeds.CancelCurrent));
    }

    [PatchPrefix]
    public static bool Prefix(BotAbstractMeds __instance)
    {
        return !MedUtils.TryCancelMedicine(__instance);
    }
}
