using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

/// <summary>
/// Sets the MedsController's operation to Finished if healing was canceled externally (e.g. getting hit)
/// This allows the operation to invoke the callback (BotMedecine.AidCallback) and set BotMedecine.Using to false,
/// or else the bot can never heal for the remainder of the raid.
/// </summary>
public class CancelHealPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotAbstractMeds).GetMethod(nameof(BotAbstractMeds.CancelCurrent));
    }

    [PatchTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalInstructions)
    {
        var instructions = new List<CodeInstruction>(originalInstructions);
        var lastIsCanceledField = AccessTools.Field(typeof(BotAbstractMeds), nameof(BotAbstractMeds._lastIsCanceled));
        if (lastIsCanceledField is null)
        {
            throw new MissingFieldException($"{nameof(BotAbstractMeds)}.{nameof(BotAbstractMeds._lastIsCanceled)} not found");
        }

        // Find instruction Stfld for _lastIsCanceled
        int i;
        for (i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.opcode == OpCodes.Stfld && instruction.operand is FieldInfo field && field == lastIsCanceledField)
            {
                break;
            }
        }

        if (i == instructions.Count)
        {
            throw new InvalidOperationException(
                $"Unable to find instruction Stfld for {nameof(BotAbstractMeds)}.{nameof(BotAbstractMeds._lastIsCanceled)}"
            );
        }

        // Insert our call to set operation finished after the instruction to set the field of _lastIsCanceled
        var ownerField = AccessTools.Field(typeof(BotAbstractMeds), nameof(BotAbstractMeds._owner));
        var setFinishedMethod = AccessTools.Method(typeof(CancelHealPatch), nameof(SetFinished));
        instructions.InsertRange(
            i + 1,
            [
                new CodeInstruction(OpCodes.Ldarg_0), // this
                new CodeInstruction(OpCodes.Ldfld, ownerField), // _owner
                new CodeInstruction(OpCodes.Call, setFinishedMethod),
            ]
        );

        return instructions;
    }

    public static void SetFinished(BotOwner owner)
    {
        if (owner.GetPlayer.HandsController is Player.MedsController medsController)
        {
            medsController._failedToApply = true;
            medsController.CurrentOperation.State = Player.EOperationState.Finished;
        }
    }
}
