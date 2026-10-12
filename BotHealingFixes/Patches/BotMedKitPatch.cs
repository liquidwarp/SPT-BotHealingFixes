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

public class BotMedKitPatch : ModulePatch
{
    private static readonly AccessTools.FieldRef<BotFirstAid, Action<BotOwner>> _onStartApplyField = AccessTools.FieldRefAccess<
        BotFirstAid,
        Action<BotOwner>
    >("_onStartApply");

    private static readonly AccessTools.FieldRef<BotFirstAid, Action<BotOwner>> _onEndApplyField = AccessTools.FieldRefAccess<
        BotFirstAid,
        Action<BotOwner>
    >("_onEndApply");

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.ApplyToSelf));
    }

    [PatchPrefix]
    public static bool PatchPrefix(BotFirstAid __instance, Action callback)
    {
        L.Debug($"BotMedKitPatch: {__instance.Name()} Start");

        _ = ApplyMedicineAsync(__instance, callback);

        return false;
    }

    // Could probably extract into a single method since it shares the same logic with BotSurgicalPatch
    public static async Task ApplyMedicineAsync(BotFirstAid __instance, Action callback)
    {
        __instance.Using = true;
        _onStartApplyField(__instance)?.Invoke(__instance._owner);
        __instance._owner.BotTalk.Say(EPhraseTrigger.StartHeal);

        var setInHandsSource = new TaskCompletionSource<Result<IMedsController>>();

        // A timeout is not needed anymore, but just to be sure...
        var timeoutSource = new CancellationTokenSource();
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(30d));
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
                L.Debug($"BotMedKitPatch: {__instance.Name()} Canceled immediately before set in hands");
            }
            else if (hands.Succeed)
            {
                L.Debug($"BotMedKitPatch: {__instance.Name()} Successful set in hands");

                var onUseSource = new TaskCompletionSource<Result<IQuickUseItem>>();
                using var onUseRegistration = timeoutSource.Token.Register(
                    (state) => ((TaskCompletionSource<Result<IQuickUseItem>>)state).TrySetCanceled(),
                    onUseSource
                );

                hands.Value.SetOnUsedCallback(onUseSource.SetResult);
                var onUseResult = await onUseSource.Task;

                __instance.FindDamagedPart();
                __instance.FirstAidApplied();
                __instance._owner.WeaponManager.Selector.TakePrevWeapon();

                L.Debug(
                    $"BotMedKitPatch: {__instance.Name()} Finished applying meds, status: {(onUseResult.Succeed ? "succeed" : $"{onUseResult.Error}")}"
                );
            }
            else
            {
                L.Warning($"BotMedKitPatch: {__instance.Name()} Failed to set in hands med item: {hands.Value?.GetType()}, {hands.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            if (!__instance._using)
            {
                L.Debug($"BotMedKitPatch: {__instance.Name()} Timed out but not healing");
                return;
            }

            __instance._owner.WeaponManager.Selector.TakePrevWeapon();

            L.Warning($"BotMedKitPatch: {__instance.Name()} Healing timed out!");
        }
        catch (Exception e)
        {
            __instance._owner.WeaponManager.Selector.TakePrevWeapon();
            L.Error($"BotMedKitPatch:\n{e}");
        }
        finally
        {
            setInHandsRegistration.Dispose();
            timeoutSource.Dispose();
        }
        L.Debug($"BotMedKitPatch: {__instance.Name()} OnEndApply");

        // Need to set time for all healing so they don't heal immediately to the next, which causes a problem for TakeWeapon.
        __instance._lastEndTime = Time.time;
        var nextPossibleUseTime = Time.time + __instance._owner.Settings.FileSettings.Mind.HEAL_DELAY_SEC;
        __instance._nextPosibleUseTime = nextPossibleUseTime;
        __instance._owner.Medecine.SurgicalKit._nextPosibleUseTime = nextPossibleUseTime;
        __instance._owner.Medecine.Stimulators._nextPosibleUseTime = Mathf.Max(
            __instance._owner.Medecine.Stimulators._nextPosibleUseTime,
            Time.time + __instance._owner.Settings.FileSettings.Mind.HEAL_DELAY_SEC
        );

        __instance.Using = false;
        __instance.RefreshMeds();
        callback?.Invoke();
        _onEndApplyField(__instance)?.Invoke(__instance._owner);
    }
}
