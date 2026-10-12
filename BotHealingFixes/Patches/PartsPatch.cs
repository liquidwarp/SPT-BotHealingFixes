using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace BotHealingFixes.Patches;

/// <summary>
/// Avoid <see cref="BotAbstractMedsToPart.parts"/> allocation and point to static array instead.
/// Fix <see cref="BotSurgicalKit.parts"/> trying to do surgery on non-surgery parts.
/// </summary>
public class PartsPatch : ModulePatch
{
    private static readonly EBodyPart[] _parts =
    [
        EBodyPart.Head,
        EBodyPart.Chest,
        EBodyPart.Stomach,
        EBodyPart.LeftArm,
        EBodyPart.RightArm,
        EBodyPart.LeftLeg,
        EBodyPart.RightLeg,
    ];

    private static readonly EBodyPart[] _surgeryParts =
    [
        EBodyPart.Stomach,
        EBodyPart.LeftArm,
        EBodyPart.RightArm,
        EBodyPart.LeftLeg,
        EBodyPart.RightLeg,
    ];

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Constructor(typeof(BotAbstractMedsToPart), [typeof(BotOwner), typeof(Action<bool>)]);
    }

    [PatchTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalInstructions)
    {
        var baseCtorInfo = AccessTools.Constructor(typeof(BotAbstractMeds), [typeof(BotOwner), typeof(Action<bool>)]);
        if (baseCtorInfo is null)
        {
            throw new MissingMethodException($"Base constructor ({nameof(BotAbstractMeds)}) not found");
        }

        // Yield return instructions up to calling the base ctor
        var foundBaseCtor = false;
        foreach (var instruction in originalInstructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is ConstructorInfo ctor && ctor == baseCtorInfo)
            {
                foundBaseCtor = true;
                yield return instruction;
                break;
            }
            yield return instruction;
        }

        if (!foundBaseCtor)
        {
            throw new InvalidOperationException($"Call to base constructor ({nameof(BotAbstractMeds)}) not found");
        }

        // Then add instructions to call our own method to set .parts
        var setPartsField = AccessTools.Method(typeof(PartsPatch), nameof(SetParts));
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Call, setPartsField);
        yield return new CodeInstruction(OpCodes.Ret);
    }

    public static void SetParts(BotAbstractMedsToPart botMeds)
    {
        if (botMeds is BotSurgicalKit)
        {
            botMeds.parts = _surgeryParts;
            return;
        }
        botMeds.parts = _parts;
    }
}
