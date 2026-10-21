using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using BuffSettings = EFT.HealthSystem.EffectsSettings.StimulatorSettings.StimulatorBuffSettings;

namespace BotHealingFixes.Utils;

public static class StimUtils
{
    private static BuffSettings[] GetBuffs(Meds meds)
    {
        var buffsName = meds.HealthEffectsComponent?.StimulatorBuffs;
        if (string.IsNullOrEmpty(buffsName))
        {
            return null;
        }
        return Singleton<GlobalConfiguration>.Instance.Health.Effects.Stimulator.Buffs.TryGetValue(buffsName, out var buffs)
            ? buffs
            : null;
    }

    public static bool StopsBleeding(Meds meds)
    {
        var buffs = GetBuffs(meds);
        if (buffs == null || DrainsHealth(buffs))
        {
            return false;
        }
        foreach (var buff in buffs)
        {
            if (buff.BuffType == EStimulatorBuffType.RemoveAllBloodLosses)
            {
                return true;
            }
        }
        return false;
    }

    public static float GetHealing(Meds meds)
    {
        var buffs = GetBuffs(meds);
        if (buffs == null || DrainsHealth(buffs))
        {
            return 0f;
        }
        var healing = 0f;
        foreach (var buff in buffs)
        {
            if (buff.BuffType == EStimulatorBuffType.HealthRate)
            {
                healing += buff.Value * buff.Duration;
            }
        }
        return healing;
    }

    public static bool IsHarmful(Meds meds)
    {
        var buffs = GetBuffs(meds);
        return buffs != null && DrainsHealth(buffs);
    }

    private static bool DrainsHealth(BuffSettings[] buffs)
    {
        foreach (var buff in buffs)
        {
            if (buff.BuffType == EStimulatorBuffType.HealthRate && buff.Value < 0f)
            {
                return true;
            }
        }
        return false;
    }

    public static Meds FindHealStim(ActiveHealthController health, List<Meds> medsList)
    {
        if (health == null || health.HasStimulatorBuff(EStimulatorBuffType.HealthRate))
        {
            return null;
        }

        Meds bestMeds = null;
        var bestStopsBleeding = true;
        var bestHealing = 0f;
        foreach (var meds in medsList)
        {
            if (meds is not Stimulator)
            {
                continue;
            }
            var healing = GetHealing(meds);
            if (healing <= 0f)
            {
                continue;
            }
            var stopsBleeding = StopsBleeding(meds);
            if (stopsBleeding == bestStopsBleeding ? healing > bestHealing : !stopsBleeding)
            {
                bestMeds = meds;
                bestStopsBleeding = stopsBleeding;
                bestHealing = healing;
            }
        }
        return bestMeds;
    }

    /// For when the AI decides to use an injector, never one that drains HP:
    ///   1. One that stops bleeds if bleeding, healing the most.
    ///   2. A healing one if hurt.
    ///   3. Otherwise the first one kept for no need, then the first one, as in vanilla.
    public static Stimulator FindStimulator(BotStimulators stimulators, List<Meds> medsList)
    {
        var health = stimulators._owner.GetPlayer.ActiveHealthController;
        if (health != null)
        {
            if (
                health.FindExistingEffect<IBleeding>() != null
                && !health.HasBloodLossBlockers()
                && FindBleedStim(medsList) is Stimulator bleedStim
            )
            {
                return bleedStim;
            }
            if (!health.GetBodyPartHealth(EBodyPart.Common).AtMaximum && FindHealStim(health, medsList) is Stimulator healStim)
            {
                return healStim;
            }
        }

        Stimulator first = null;
        foreach (var meds in medsList)
        {
            if (meds is not Stimulator stimulator || IsHarmful(stimulator))
            {
                continue;
            }
            if (!StopsBleeding(stimulator) && GetHealing(stimulator) <= 0f)
            {
                return stimulator;
            }
            first ??= stimulator;
        }
        return first;
    }

    private static Meds FindBleedStim(List<Meds> medsList)
    {
        Meds bestMeds = null;
        var bestHealing = -1f;
        foreach (var meds in medsList)
        {
            if (meds is not Stimulator || !StopsBleeding(meds))
            {
                continue;
            }
            var healing = GetHealing(meds);
            if (healing > bestHealing)
            {
                bestMeds = meds;
                bestHealing = healing;
            }
        }
        return bestMeds;
    }
}
