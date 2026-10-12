using System;
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
            setInHandsRegistration.Dispose();
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

        switch (botMeds)
        {
            case BotFirstAid firstAid:
                if (successful)
                {
                    firstAid.FindDamagedPart();
                    firstAid.FirstAidApplied();
                }
                firstAid.RefreshMeds();
                firstAid._lastEndTime = Time.time;
                _onEndApplyField(firstAid)?.Invoke(botMeds._owner);
                break;
            case BotSurgicalKit surgical:
                if (successful)
                {
                    surgical.FindDamagedPart();
                    surgical._owner.Medecine.FirstAid.FindDamagedPart();
                }
                surgical.RefreshMeds();
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

    public enum EBleedType
    {
        None,
        Light,
        Heavy,
    }
}
