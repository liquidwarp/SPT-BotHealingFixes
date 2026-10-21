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

    public static async Task ApplyMedicineAsync(this BotAbstractMedsToPart botMeds, TimeSpan timeout, Action callback)
    {
        L.Debug($"{botMeds.GetType()}: {botMeds.Name()} Start");

        botMeds.Using = true;
        botMeds._owner.BotTalk.Say(EPhraseTrigger.StartHeal);
        if (botMeds is BotFirstAid firstAid)
        {
            _onStartApplyField(firstAid)?.Invoke(firstAid._owner);
        }

        var setInHandsSource = new TaskCompletionSource<Result<IMedsController>>();

        // A timeout is probably not needed anymore, but just to be sure...
        var timeoutSource = new CancellationTokenSource();
        timeoutSource.CancelAfter(timeout);
        var setInHandsRegistration = timeoutSource.Token.Register(
            (state) => ((TaskCompletionSource<Result<IMedsController>>)state).TrySetCanceled(),
            setInHandsSource
        );

        botMeds._owner.GetPlayer.SetInHands(
            botMeds.CurUsingMeds,
            botMeds._bodyPartToHeal!.Value,
            0, // Actually unused
            setInHandsSource.SetResult
        );
        var successful = false;
        try
        {
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

    public static void OnEndApply(BotAbstractMedsToPart botMeds, bool successful, Action callback)
    {
        L.Debug($"{botMeds.GetType()}: {botMeds.Name()} OnEndApply");

        botMeds.Using = false;

        // Need to set time for all healing so they don't heal immediately to the next, which causes a problem for TakeWeapon.
        var nextPossibleUseTime = Time.time + botMeds._owner.Settings.FileSettings.Mind.HEAL_DELAY_SEC;
        botMeds._owner.Medecine.FirstAid._nextPosibleUseTime = nextPossibleUseTime;
        botMeds._owner.Medecine.SurgicalKit._nextPosibleUseTime = nextPossibleUseTime;
        botMeds._owner.Medecine.Stimulators._nextPosibleUseTime = Mathf.Max(
            botMeds._owner.Medecine.Stimulators._nextPosibleUseTime,
            nextPossibleUseTime
        );

        // Items can do both first aid and surgery, so whichever was used, the other's pick may no longer afford its job.
        // (e.g. from mod MakeMedsGreatAgain)
        // Re-check damaged parts after refreshing so IsBleeding reflects the newly picked item.
        var medecine = botMeds._owner.Medecine;
        switch (botMeds)
        {
            case BotFirstAid firstAid:
                firstAid.RefreshMeds();
                firstAid.FindDamagedPart();
                if (successful)
                {
                    firstAid.FirstAidApplied();
                }
                medecine.SurgicalKit.RefreshMeds();
                firstAid._lastEndTime = Time.time;
                _onEndApplyField(firstAid)?.Invoke(botMeds._owner);
                break;
            case BotSurgicalKit surgical:
                if (successful)
                {
                    surgical.FindDamagedPart();
                }
                surgical.RefreshMeds();
                medecine.FirstAid.RefreshMeds();
                medecine.FirstAid.FindDamagedPart();
                break;
        }
        callback?.Invoke();
    }

    public static EBleedType GetBleedType(this IHealthController health, EBodyPart part = EBodyPart.Common)
    {
        return health.FindActiveEffect<IBleeding>(part) switch
        {
            ILightBleeding => EBleedType.Light,
            IHeavyBleeding => EBleedType.Heavy,
            _ => EBleedType.None,
        };
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

    /// Tourniquets and bandages first, then medkits so their HP goes to healing, then surgical kits so their uses go to surgery.
    public static int GetBleedMedsRank(Meds meds)
    {
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

    public static bool CanTreat(this IHealthController health, Meds meds, EDamageEffectType damageEffect, EBodyPart bodyPart)
    {
        return CanAfford(meds, damageEffect) && health.CanApplyItem(meds, bodyPart);
    }

    /// Dedicated surgical kits (CMS, Surv12) before medkits, which mods like MakeMedsGreatAgain give surgery, so surgery doesn't
    /// drain the bot's HP healing.
    public static Meds FindSurgeryMeds(List<Meds> medsList)
    {
        Meds medKit = null;
        foreach (var meds in medsList)
        {
            if (meds is MedKit && medKit != null)
            {
                continue;
            }
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

    /// Heavy before light, among the bleed types the item lists, whatever their cost.
    public static Meds FindBleedMeds(this IHealthController health, List<Meds> medsList)
    {
        var heavyBleeding = health.FindExistingEffect<IHeavyBleeding>() != null;
        var lightBleeding = health.FindExistingEffect<ILightBleeding>() != null;
        if (!heavyBleeding && !lightBleeding)
        {
            return null;
        }

        Meds bestMeds = null;
        var bestRank = int.MaxValue;
        foreach (var meds in medsList)
        {
            var rank = GetBleedMedsRank(meds);
            if (rank < bestRank && CanTreatTargetBleed(meds, heavyBleeding, lightBleeding))
            {
                bestMeds = meds;
                bestRank = rank;
                if (rank == 0)
                {
                    break;
                }
            }
        }
        return bestMeds;
    }

    private static bool CanTreatTargetBleed(Meds meds, bool heavyBleeding, bool lightBleeding)
    {
        if (heavyBleeding && meds.HealthEffectsComponent.Affects(EDamageEffectType.HeavyBleeding, out _))
        {
            return CanAfford(meds, EDamageEffectType.HeavyBleeding);
        }
        return lightBleeding && CanAfford(meds, EDamageEffectType.LightBleeding);
    }

    public enum EBleedType
    {
        None,
        Light,
        Heavy,
    }
}
