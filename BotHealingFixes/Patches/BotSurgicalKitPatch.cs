using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BotHealingFixes.Utils;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BotHealingFixes.Patches;

public class BotSurgicalKitPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotSurgicalKit), nameof(BotSurgicalKit.ApplyToCurrentPart));
    }

    [PatchPrefix]
    public static bool PatchPrefix(BotSurgicalKit __instance, Action callbackEndUse)
    {
        if (
            !__instance._bodyPartToHeal.HasValue
            || !__instance.HaveSmth2Use
            || __instance._owner.WeaponManager.Reload.Reloading
            || !__instance._owner.GetPlayer.HealthController.CanApplyItem(__instance.CurUsingMeds, __instance._bodyPartToHeal.Value)
        )
        {
            return false;
        }

        L.Debug($"BotSurgicalKitPatch: {__instance.Name()} Start");

        _ = ApplyMedicineAsync(__instance, callbackEndUse);

        return false;
    }

    public static async Task ApplyMedicineAsync(BotSurgicalKit __instance, Action callback)
    {
        __instance.Using = true;
        __instance._owner.BotTalk.Say(EPhraseTrigger.StartHeal);

        var setInHandsSource = new TaskCompletionSource<Result<IMedsController>>();

        // A timeout is not needed anymore, but just to be sure...
        var timeoutSource = new CancellationTokenSource();
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(60d));
        var setInHandsRegistration = timeoutSource.Token.Register(
            (state) => ((TaskCompletionSource<Result<IMedsController>>)state).TrySetCanceled(),
            setInHandsSource
        );

        __instance._owner.GetPlayer.SetInHands(
            __instance.CurUsingMeds,
            __instance._bodyPartToHeal!.Value,
            0, // Actually unused
            setInHandsSource.SetResult
        );
        try
        {
            var hands = await setInHandsSource.Task;
            if (__instance._lastIsCanceled)
            {
                // Edge case where CancelCurrent is called, but we haven't set the callback yet.
                // CancelHealPatch won't catch this so check if healing hasn't been canceled.
                L.Debug($"BotSurgicalKitPatch: {__instance.Name()} Canceled immediately before set in hands");
            }
            else if (hands.Succeed)
            {
                L.Debug($"BotSurgicalKitPatch: {__instance.Name()} Successful set in hands");

                var onUseSource = new TaskCompletionSource<Result<IQuickUseItem>>();
                using var onUseRegistration = timeoutSource.Token.Register(
                    (state) => ((TaskCompletionSource<Result<IQuickUseItem>>)state).TrySetCanceled(),
                    onUseSource
                );

                hands.Value.SetOnUsedCallback(onUseSource.SetResult);
                var onUseResult = await onUseSource.Task;

                __instance.FindDamagedPart();
                __instance._owner.Medecine.FirstAid.FindDamagedPart();
                __instance._owner.WeaponManager.Selector.TakePrevWeapon();

                L.Debug(
                    $"BotSurgicalKitPatch: {__instance.Name()} Finished applying meds, status: {(onUseResult.Succeed ? "succeed" : $"{onUseResult.Error}")}"
                );
            }
            else
            {
                L.Warning(
                    $"BotSurgicalKitPatch: {__instance.Name()} Failed to set in hands med item: {hands.Value?.GetType()}, {hands.Error}"
                );
            }
        }
        catch (OperationCanceledException)
        {
            if (!__instance._using)
            {
                L.Debug($"BotSurgicalKitPatch: {__instance.Name()} Timed out but not healing");
                return;
            }

            __instance._owner.WeaponManager.Selector.TakePrevWeapon();
            L.Warning($"BotSurgicalKitPatch: {__instance.Name()} Healing timed out!");
        }
        catch (Exception e)
        {
            __instance._owner.WeaponManager.Selector.TakePrevWeapon();
            L.Error($"BotSurgicalKitPatch:\n{e}");
        }
        finally
        {
            setInHandsRegistration.Dispose();
            timeoutSource.Dispose();
        }
        L.Debug($"BotSurgicalKitPatch: {__instance.Name()} OnEndApply");

        // Need to set time for all healing so they don't heal immediately to the next, which causes a problem for TakeWeapon.
        var nextPossibleUseTime = Time.time + __instance._owner.Settings.FileSettings.Mind.HEAL_DELAY_SEC;
        __instance._nextPosibleUseTime = nextPossibleUseTime;
        __instance._owner.Medecine.FirstAid._nextPosibleUseTime = nextPossibleUseTime;
        __instance._owner.Medecine.Stimulators._nextPosibleUseTime = Mathf.Max(
            __instance._owner.Medecine.Stimulators._nextPosibleUseTime,
            Time.time + __instance._owner.Settings.FileSettings.Mind.HEAL_DELAY_SEC
        );

        __instance.Using = false;
        __instance.RefreshMeds();
        callback?.Invoke();
    }
}
