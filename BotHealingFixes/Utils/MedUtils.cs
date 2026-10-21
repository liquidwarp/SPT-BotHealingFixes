using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace BotHealingFixes.Utils;

public static class MedUtils
{
    private static readonly AccessTools.FieldRef<BotFirstAid, Action<BotOwner>> _onStartApplyField = AccessTools.FieldRefAccess<
        BotFirstAid,
        Action<BotOwner>
    >("_onStartApply");

    private static readonly AccessTools.FieldRef<BotFirstAid, Action<BotOwner>> _onEndApplyField = AccessTools.FieldRefAccess<
        BotFirstAid,
        Action<BotOwner>
    >("_onEndApply");

    public static Task ApplyMedicineAsync(this BotAbstractMedsToPart botMeds, TimeSpan timeout, Action callback)
    {
        return botMeds.ApplyMedicineAsync(botMeds.CurUsingMeds, botMeds._bodyPartToHeal!.Value, timeout, callback);
    }

    public static async Task ApplyMedicineAsync(this BotAbstractMeds botMeds, Meds meds, EBodyPart bodyPart, TimeSpan timeout, Action callback)
    {
        L.Debug($"{botMeds.GetType()}: {botMeds.Name()} Start");

        var setInHandsSource = new TaskCompletionSource<Result<IMedsController>>();

        // A timeout is probably not needed anymore, but just to be sure...
        var timeoutSource = new CancellationTokenSource();
        timeoutSource.CancelAfter(timeout);
        var setInHandsRegistration = timeoutSource.Token.Register(
            (state) => ((TaskCompletionSource<Result<IMedsController>>)state).TrySetCanceled(),
            setInHandsSource
        );

        botMeds.Using = true;
        var successful = false;
        try
        {
            botMeds._owner.BotTalk.Say(EPhraseTrigger.StartHeal);
            switch (botMeds)
            {
                case BotFirstAid firstAid:
                    _onStartApplyField(firstAid)?.Invoke(firstAid._owner);
                    break;
                case BotStimulators stimulators:
                    stimulators.LastUseTime = Time.time;
                    break;
            }

            botMeds._owner.GetPlayer.SetInHands(
                meds,
                bodyPart,
                0, // Actually unused
                setInHandsSource.SetResult
            );
            var hands = await setInHandsSource.Task;
            if (botMeds._lastIsCanceled)
            {
                // Edge case where CancelCurrent is called, but we haven't set the callback yet.
                // CancelHealPatch won't catch this so check if healing hasn't been canceled.
                L.Debug($"{botMeds.GetType()}: {botMeds.Name()} Canceled immediately before set in hands");
            }
            else if (hands.Succeed)
            {
                L.Debug($"{botMeds.GetType()}: {botMeds.Name()} Successful set in hands");

                var onUseSource = new TaskCompletionSource<Result<IQuickUseItem>>();
                using var onUseRegistration = timeoutSource.Token.Register(
                    (state) => ((TaskCompletionSource<Result<IQuickUseItem>>)state).TrySetCanceled(),
                    onUseSource
                );

                hands.Value.SetOnUsedCallback(onUseSource.SetResult);
                await onUseSource.Task;

                if (hands.Succeed && hands.Value is Player.MedsController medsController)
                {
                    successful = !medsController._failedToApply;
                }
                botMeds._owner.WeaponManager.Selector.TakePrevWeapon();

                L.Debug($"{botMeds.GetType()}: {botMeds.Name()} Finished applying meds, status: {successful}");
            }
            else
            {
                L.Warning(
                    $"{botMeds.GetType()}: {botMeds.Name()} Failed to set in hands med item: {hands.Value?.GetType()}, {hands.Error}"
                );
            }
        }
        catch (OperationCanceledException)
        {
            if (!botMeds._using)
            {
                L.Debug($"{botMeds.GetType()}: {botMeds.Name()} Timed out but not healing");
                return;
            }

            botMeds._owner.WeaponManager.Selector.TakePrevWeapon();
            L.Warning($"{botMeds.GetType()}: {botMeds.Name()} Healing timed out!");
        }
        catch (Exception e)
        {
            botMeds._owner.WeaponManager.Selector.TakePrevWeapon();
            L.Error($"{botMeds.GetType()}:\n{e}");
        }
        finally
        {
            await setInHandsRegistration.DisposeAsync();
            timeoutSource.Dispose();
        }

        OnEndApply(botMeds, successful, callback);
    }

    public static void OnEndApply(BotAbstractMeds botMeds, bool successful, Action callback)
    {
        L.Debug($"{botMeds.GetType()}: {botMeds.Name()} OnEndApply");

        botMeds.Using = false;

        // Need to set time for all healing so they don't heal immediately to the next, which causes a problem for TakeWeapon.
        var medecine = botMeds._owner.Medecine;
        var mind = botMeds._owner.Settings.FileSettings.Mind;
        var nextPossibleUseTime = Time.time + mind.HEAL_DELAY_SEC;
        medecine.FirstAid._nextPosibleUseTime = nextPossibleUseTime;
        medecine.SurgicalKit._nextPosibleUseTime = nextPossibleUseTime;
        medecine.Stimulators._nextPosibleUseTime = Mathf.Max(medecine.Stimulators._nextPosibleUseTime, nextPossibleUseTime);

        // Whichever was used, refresh every pick: items can do both first aid and surgery, so the other's may no longer afford its
        // job (e.g. from mod MakeMedsGreatAgain), and first aid can fall back to an injector.
        medecine.FirstAid.RefreshAndFindDamagedPart();
        switch (botMeds)
        {
            case BotFirstAid firstAid:
                if (successful)
                {
                    firstAid.FirstAidApplied();
                }
                firstAid._lastEndTime = Time.time;
                _onEndApplyField(firstAid)?.Invoke(botMeds._owner);
                break;
            case BotStimulators stimulators:
                stimulators.LastEndUseTime = Time.time;
                stimulators._nextPosibleUseTime = Mathf.Max(nextPossibleUseTime, Time.time + mind.FOOD_DRINK_DELAY_SEC);
                break;
        }
        medecine.SurgicalKit.RefreshMeds();
        medecine.SurgicalKit.FindDamagedPart();
        medecine.Stimulators.FindMeds();

        if (botMeds is not BotStimulators || successful)
        {
            callback?.Invoke();
        }
    }

    public static void RefreshAndFindDamagedPart(this BotFirstAid firstAid)
    {
        firstAid.RefreshMeds();
        firstAid.FindDamagedPart();
    }

    public static bool Affects(this HealthEffectsComponent healthEffects, EDamageEffectType damageEffect, out float cost)
    {
        if (healthEffects.DamageEffects.TryGetValue(damageEffect, out var props))
        {
            cost = props.Cost;
            return true;
        }
        cost = 0;
        return false;
    }

    public static List<Meds> GetAvailableMeds(this BotAbstractMeds botMeds, bool shallUseInSafe)
    {
        var medsList = botMeds._medsList;
        medsList.Clear();
        botMeds._owner.GetPlayer.InventoryController.GetAcceptableItemsNonAlloc(
            shallUseInSafe ? BotMedecine.secureSlots : BotMedecine.anySlots,
            medsList
        );
        return medsList;
    }

    private static int GetBleedMedsRank(Meds meds)
    {
        if (meds is Stimulator)
        {
            return 3;
        }
        if (meds is MedKit)
        {
            return 1;
        }
        return meds.HealthEffectsComponent.Affects(EDamageEffectType.DestroyedPart, out _) ? 2 : 0;
    }

    public static bool CanAfford(Meds meds, EDamageEffectType damageEffect)
    {
        return meds.HealthEffectsComponent.Affects(damageEffect, out var cost)
            && (meds.MedKitComponent == null || cost <= meds.MedKitComponent.HpResource);
    }

    public static bool CanTreat(Meds meds, EDamageEffectType damageEffect)
    {
        if (meds is Stimulator)
        {
            return damageEffect is EDamageEffectType.HeavyBleeding or EDamageEffectType.LightBleeding && StimUtils.StopsBleeding(meds);
        }
        return CanAfford(meds, damageEffect);
    }

    /// Dedicated surgical kits (CMS, Surv12) before medkits, which mods like MakeMedsGreatAgain give surgery, so surgery doesn't
    /// drain the bot's HP healing.
    public static Meds FindSurgeryMeds(List<Meds> medsList)
    {
        Meds medKit = null;
        foreach (var meds in medsList)
        {
            if (!CanAfford(meds, EDamageEffectType.DestroyedPart))
            {
                continue;
            }
            if (meds is not MedKit)
            {
                return meds;
            }
            medKit ??= meds;
        }
        return medKit;
    }

    public static Meds FindBleedMeds(this IHealthController health, List<Meds> medsList)
    {
        const int lightOnlyScore = 4;

        var heavyBleeding = health.FindExistingEffect<IHeavyBleeding>() != null;
        var lightBleeding = health.FindExistingEffect<ILightBleeding>() != null;
        if (!heavyBleeding && !lightBleeding)
        {
            return null;
        }

        var bestPossibleScore = heavyBleeding ? 0 : lightOnlyScore;
        Meds bestMeds = null;
        var bestScore = int.MaxValue;
        foreach (var meds in medsList)
        {
            var score = GetBleedMedsRank(meds);
            if (!heavyBleeding || !CanTreat(meds, EDamageEffectType.HeavyBleeding))
            {
                if (!lightBleeding || !CanTreat(meds, EDamageEffectType.LightBleeding))
                {
                    continue;
                }
                score += lightOnlyScore;
            }

            if (score < bestScore)
            {
                bestMeds = meds;
                bestScore = score;
                if (score == bestPossibleScore)
                {
                    break;
                }
            }
        }
        return bestMeds;
    }
}
