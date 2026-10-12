#if DEBUG
using System.Text;
using BotHealingFixes.Utils;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace BotHealingFixes.Brain;

public class DebugHealLayer(BotOwner botOwner, int priority) : CustomLayer(botOwner, priority)
{
    private readonly Action _healAction = new(typeof(DebugHealLogic), "DebugHealLogic");

    private readonly BotMedecine _meds = botOwner.Medecine;

    public override string GetName()
    {
        return "DebugHealLayer";
    }

    public override bool IsActive()
    {
        return true;
    }

    public override Action GetNextAction()
    {
        return _healAction;
    }

    public override bool IsCurrentActionEnding()
    {
        return false;
    }

    public override void BuildDebugText(StringBuilder stringBuilder)
    {
        stringBuilder.AppendLine();
        stringBuilder.AppendLabeledValue("Medicine Using", $"{_meds.Using}", Color.white, GetIsTrueColor(_meds.Using));

        stringBuilder.AppendLine();
        stringBuilder.AppendLine("MedKit");
        var firstAid = _meds.FirstAid;
        var canUseFirstAidByTime = Time.time > firstAid._nextPosibleUseTime;
        stringBuilder.AppendLabeledValue("Using", $"{firstAid.Using}", Color.white, GetIsTrueColor(firstAid.Using));
        stringBuilder.AppendLabeledValue("Have Work", $"{firstAid.Have2Do}", Color.white, GetIsTrueColor(!firstAid.Have2Do));
        stringBuilder.AppendLabeledValue("Can Use by Time", $"{canUseFirstAidByTime}", Color.white, GetIsTrueColor(canUseFirstAidByTime));
        stringBuilder.AppendLabeledValue("Damaged", $"{firstAid.Damaged}", Color.white, GetIsTrueColor(!firstAid.Damaged));
        stringBuilder.AppendLabeledValue("Bleeding", $"{firstAid.IsBleeding}", Color.white, GetIsTrueColor(!firstAid.IsBleeding));
        stringBuilder.AppendLabeledValue("Part", GetPartToHeal(firstAid), Color.white, Color.white);
        GetItem(stringBuilder, firstAid.CurUsingMeds);

        stringBuilder.AppendLine();
        stringBuilder.AppendLine("Surgery");
        var surgery = _meds.SurgicalKit;
        var canDoSurgeryByTime = Time.time > surgery._nextPosibleUseTime;
        stringBuilder.AppendLabeledValue("Using", $"{surgery.Using}", Color.white, GetIsTrueColor(surgery.Using));
        stringBuilder.AppendLabeledValue("Have Work", $"{surgery.HaveWork}", Color.white, GetIsTrueColor(!surgery.HaveWork));
        stringBuilder.AppendLabeledValue("Can Use by Time", $"{canDoSurgeryByTime}", Color.white, GetIsTrueColor(canDoSurgeryByTime));
        stringBuilder.AppendLabeledValue("Damaged", $"{surgery.Damaged}", Color.white, GetIsTrueColor(!surgery.Damaged));
        stringBuilder.AppendLabeledValue("Part", GetPartToHeal(surgery), Color.white, Color.white);
        GetItem(stringBuilder, surgery.CurUsingMeds);

        stringBuilder.AppendLine();
        stringBuilder.AppendLine("Health");
        var debugStruct = new DebugBotStruct();
        debugStruct.SetHealthData(BotOwner.HealthController);
        GetHealth(stringBuilder, debugStruct);
    }

    private static Color GetIsTrueColor(bool isTrue)
    {
        return isTrue ? Color.green : Color.red;
    }

    private static string GetPartToHeal(BotAbstractMedsToPart surgical)
    {
        return surgical._bodyPartToHeal.HasValue ? surgical._bodyPartToHeal.Value.ToString() : "None";
    }

    private static void GetItem(StringBuilder stringBuilder, Meds medKit)
    {
        if (medKit is not null)
        {
            stringBuilder.AppendLabeledValue("Item", $"{medKit.LocalizedShortName()}", Color.white, Color.green);
            var hp = medKit.MedKitComponent?.HpResource;
            stringBuilder.AppendLabeledValue("Item HP", $"{hp}", Color.white, Color.white);
        }
        else
        {
            stringBuilder.AppendLabeledValue("Item", "None", Color.white, Color.red);
        }
    }

    /// <see cref="BotInfoDataPanel.GetHealthData"/>
    private void GetHealth(StringBuilder stringBuilder, DebugBotStruct debugBotStruct)
    {
        var healthData = debugBotStruct.HeathsData;

        stringBuilder.AppendLabeledValue(
            "Head",
            $"{healthData.HealthHead}{GetBleedLabel(EBodyPart.Head)}{GetBrokenLabel(healthData.HealthHeadBroken)}/{healthData.MaxHealthHead}",
            Color.white,
            Color.white
        );
        stringBuilder.AppendLabeledValue(
            "Chest",
            $"{healthData.HealthBody}{GetBleedLabel(EBodyPart.Chest)}{GetBrokenLabel(healthData.HealthBodyBroken)}/{healthData.MaxHealthBody}",
            Color.white,
            Color.white
        );
        stringBuilder.AppendLabeledValue(
            "Stomach",
            $"{healthData.HealthStomach}{GetBleedLabel(EBodyPart.Stomach)}{GetBrokenLabel(healthData.HealthStomachBroken)}/{healthData.MaxHealthStomach}",
            Color.white,
            Color.white
        );
        stringBuilder.AppendLabeledValue(
            "Arms",
            $"{healthData.HealthLeftArm}{GetBleedLabel(EBodyPart.LeftArm)}{GetBrokenLabel(healthData.HealthLeftArmBroken)}/{healthData.MaxHealthLeftArm} {healthData.HealthRightArm}{GetBleedLabel(EBodyPart.RightArm)}{GetBrokenLabel(healthData.HealthRightArmBroken)}/{healthData.MaxHealthRightArm}",
            Color.white,
            Color.white
        );
        stringBuilder.AppendLabeledValue(
            "Legs",
            $"{healthData.HealthLeftLeg}{GetBleedLabel(EBodyPart.LeftLeg)}{GetBrokenLabel(healthData.HealthLeftLegBroken)}/{healthData.MaxHealthLeftLeg} {healthData.HealthRightLeg}{GetBleedLabel(EBodyPart.RightLeg)}{GetBrokenLabel(healthData.HealthRightLegBroken)}/{healthData.MaxHealthRightLeg}",
            Color.white,
            Color.white
        );
    }

    private string GetBleedLabel(EBodyPart bodyPart)
    {
        return BotOwner.HealthController.GetBleedType(bodyPart) switch
        {
            MedUtils.EBleedType.Light => "(LBL)",
            MedUtils.EBleedType.Heavy => "(HBL)",
            _ => string.Empty,
        };
    }

    private static string GetBrokenLabel(bool val)
    {
        return val ? "(Broken)" : string.Empty;
    }
}
#endif
