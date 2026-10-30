using System;
using System.Collections.Generic;
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

    // Reused for the bot's lifetime: callbacks and retry checks allocate nothing per treatment.
    private sealed class MedicalApplication
    {
        private enum Phase
        {
            Idle,
            Equipping,
            Using,
            RestorePending,
            Restored,
        }

        private readonly BotOwner _owner;
        private readonly Callback<IMedsController> _onEquipped;
        private readonly Callback<IQuickUseItem> _onUsed;
        private BotAbstractMeds _meds;
        private Player.MedsController _controller;
        private Phase _phase;
        private Meds _item;
        private Action _callback;
        private Action<bool> _stimCallback;
        private float _deadline;
        private bool _cancelled;
        private bool _successful;

        public MedicalApplication(BotOwner owner)
        {
            _owner = owner;
            _onEquipped = OnEquipped;
            _onUsed = OnUsed;
        }

        public void Start(BotAbstractMeds meds, Meds item, EBodyPart bodyPart, float timeout, Action callback, Action<bool> stimCallback)
        {
            if (_phase != Phase.Idle)
            {
                // Another treatment still owns the hands; report the failed start as the TryApply prefix does.
                stimCallback?.Invoke(false);
                return;
            }
            _meds = meds;
            _item = item;
            _callback = callback;
            _stimCallback = stimCallback;
            _controller = null;
            _cancelled = false;
            _successful = false;
            _deadline = Time.realtimeSinceStartup + timeout;
            _phase = Phase.Equipping;
            _activeApplications.Add(this);
            meds.Using = true;
            try
            {
                _owner.BotTalk.Say(EPhraseTrigger.StartHeal);
                switch (meds)
                {
                    case BotFirstAid firstAid:
                        _onStartApplyField(firstAid)?.Invoke(_owner);
                        break;
                    case BotStimulators stimulators:
                        stimulators.LastUseTime = Time.time;
                        break;
                }
                _owner.GetPlayer.SetInHands(item, bodyPart, 0, _onEquipped);
            }
            catch (Exception e)
            {
                HandleFailure(e);
            }
        }

        public bool Owns(BotAbstractMeds meds)
        {
            return ReferenceEquals(_meds, meds);
        }

        public void Cancel()
        {
            // Use has already ended and the weapon is being restored; there is nothing left to cancel.
            if (_phase >= Phase.RestorePending)
                return;
            if (!_meds._lastIsCanceled && _meds._using)
            {
                if (_meds._nextPosibleUseTime < Time.time + 3f)
                    _meds._nextPosibleUseTime += 5f;
                _meds._lastIsCanceled = true;
            }
            _cancelled = true;
            var player = _owner.GetPlayer;
            if (_controller == null || player == null || !ReferenceEquals(player.HandsController, _controller))
                return;
            _controller._failedToApply = true;
            var operation = _controller.CurrentOperation;
            if (operation._finished)
                return;
            // Run medical cleanup and completion callbacks instead of only assigning State.
            operation.Remove();
            operation.Finish();
        }

        private void OnEquipped(Result<IMedsController> hands)
        {
            if (_phase != Phase.Equipping)
                return;
            try
            {
                var player = _owner.GetPlayer;
                if (player == null || player.HealthController?.IsAlive != true)
                {
                    Abandon();
                    return;
                }
                _controller = hands.Succeed ? hands.Value as Player.MedsController : null;
                if (!hands.Succeed)
                {
                    L.Warning($"{_meds.GetType()}: {_meds.Name()} Failed to set in hands med item: {hands.Error}");
                    if (player.HandsController is Player.MedsController existing && ReferenceEquals(existing.Item, _item))
                    {
                        _controller = existing;
                        _cancelled = true;
                    }
                }
                if (_controller == null || !ReferenceEquals(player.HandsController, _controller))
                {
                    // Finish on the update tick, after EFT's equip callback has unwound.
                    _phase = Phase.RestorePending;
                    return;
                }
                _phase = Phase.Using;
                _controller.SetOnUsedCallback(_onUsed);
                // The operation can finish before the equip callback registers its use callback.
                if (_phase != Phase.Using)
                    return;
                if (_controller.CurrentOperation._finished || _controller._failedToApply)
                {
                    _successful = !_cancelled && !_controller._failedToApply;
                    _phase = Phase.RestorePending;
                }
                // A cancel that arrived while equipping is applied from the update tick, after this callback has unwound.
            }
            catch (Exception e)
            {
                HandleFailure(e);
            }
        }

        private void OnUsed(Result<IQuickUseItem> used)
        {
            if (_phase != Phase.Using)
                return;
            _successful = used.Succeed && !_cancelled && !_controller._failedToApply;
            // Restore from the update tick, after the operation's callback stack has unwound.
            _phase = Phase.RestorePending;
        }

        public void Tick(float now)
        {
            if (_phase == Phase.Idle)
                return;
            try
            {
                var player = _owner.GetPlayer;
                if (player == null || player.HealthController?.IsAlive != true)
                {
                    Abandon();
                    return;
                }
                if (_phase == Phase.Using && _cancelled)
                {
                    Cancel();
                }
                if ((_phase == Phase.Equipping || _phase == Phase.Using) && now >= _deadline)
                {
                    if (!_cancelled)
                    {
                        L.Warning($"{_meds.GetType()}: {_meds.Name()} Healing timed out; cancelling and retaining ownership until hands finish");
                        Cancel();
                    }
                    // A missing equip or use callback leaves nothing in flight once EFT's hands process is idle.
                    if ((_phase == Phase.Equipping || _phase == Phase.Using) && player.ProcessStatus == Player.EProcessStatus.None)
                    {
                        var current = player.HandsController as Player.MedsController;
                        if (current != null && ReferenceEquals(current.Item, _item))
                        {
                            _controller = current;
                            Cancel();
                        }
                        _phase = Phase.RestorePending;
                    }
                }
                if (_phase == Phase.RestorePending)
                {
                    if (!ReferenceEquals(player.HandsController, _controller))
                    {
                        _phase = Phase.Restored;
                    }
                    else if (player.ProcessStatus == Player.EProcessStatus.None)
                    {
                        // The selector's swap throttles and reload flag are AI state; TakeMainWeapon skips them and
                        // still proceeds through Player.SetSlotItem, and EFT retries it if the weapon can't be taken.
                        // A swap that starts sets ProcessStatus synchronously; an empty slot is accepted but starts nothing.
                        var selector = _owner.WeaponManager.Selector;
                        if (!selector.TakePrevWeapon()
                            || (player.ProcessStatus == Player.EProcessStatus.None && ReferenceEquals(player.HandsController, _controller)))
                        {
                            L.Warning($"{_meds.GetType()}: {_meds.Name()} Previous weapon unavailable; taking main weapon");
                            selector.TakeMainWeapon();
                        }
                        _phase = Phase.Restored;
                    }
                }
                // Nothing is in flight once EFT's hands process is idle. If no weapon could be drawn, the used item stays
                // in hands as before this application, but the bot must still be free to heal again.
                if (_phase == Phase.Restored && player.ProcessStatus == Player.EProcessStatus.None)
                {
                    if (_controller != null && ReferenceEquals(player.HandsController, _controller))
                        L.Warning($"{_meds.GetType()}: {_meds.Name()} No weapon could be drawn; releasing with the item in hands");
                    Complete();
                }
            }
            catch (Exception e)
            {
                HandleFailure(e);
            }
        }

        private void HandleFailure(Exception e)
        {
            L.Error($"{_meds?.GetType()}:\n{e}");
            if (_phase == Phase.Idle)
                return;
            Cancel();
            if (_controller != null)
                _phase = Phase.RestorePending;
            else if (_owner.GetPlayer != null && _owner.GetPlayer.ProcessStatus == Player.EProcessStatus.None)
                _phase = Phase.Restored;
            // Otherwise an equip transition still owns hands; wait for its callback.
        }

        public void Abandon()
        {
            if (_phase == Phase.Idle)
                return;
            _activeApplications.Remove(this);
            _phase = Phase.Idle;
            _callback = null;
            _stimCallback = null;
            _item = null;
            _controller = null;
            _meds = null;
        }

        private void Complete()
        {
            var meds = _meds;
            var callback = _callback;
            var stimCallback = _stimCallback;
            bool successful = _successful;
            Abandon();
            try
            {
                OnEndApply(meds, successful, callback);
                if (successful)
                    stimCallback?.Invoke(true);
            }
            catch (Exception e)
            {
                // Completion has released ownership; a caller's exception must not cancel a new treatment.
                L.Error($"{meds.GetType()}:\n{e}");
            }
        }
    }

    private static readonly Dictionary<BotOwner, MedicalApplication> _applications = new();
    // Only treatments in progress are ticked; an application joins on Start and leaves on Abandon.
    private static readonly List<MedicalApplication> _activeApplications = new();

    private static MedicalApplication GetApplication(BotOwner owner)
    {
        if (_applications.TryGetValue(owner, out var application))
            return application;
        application = new MedicalApplication(owner);
        _applications.Add(owner, application);
        return application;
    }

    public static void ForgetBot(BotMedecine medicine)
    {
        var owner = medicine.FirstAid._owner;
        if (_applications.TryGetValue(owner, out var application))
        {
            application.Abandon();
            _applications.Remove(owner);
        }
    }

    public static void UpdateApplications(float now)
    {
        // Reverse indexed traversal: completing removes the current entry, and callbacks may remove or add others.
        for (int i = _activeApplications.Count - 1; i >= 0; i--)
        {
            if (i < _activeApplications.Count)
                _activeApplications[i].Tick(now);
        }
    }

    internal static bool TryCancelMedicine(BotAbstractMeds meds)
    {
        if (!_applications.TryGetValue(meds._owner, out var application) || !application.Owns(meds))
            return false;
        application.Cancel();
        return true;
    }

    public static void ApplyMedicine(this BotAbstractMedsToPart botMeds, float timeout, Action callback)
    {
        botMeds.ApplyMedicine(botMeds.CurUsingMeds, botMeds._bodyPartToHeal!.Value, timeout, callback);
    }

    public static void ApplyMedicine(
        this BotAbstractMeds botMeds,
        Meds meds,
        EBodyPart bodyPart,
        float timeout,
        Action callback,
        Action<bool> stimCallback = null)
    {
        // Created on a bot's first treatment, so bots that never heal cost nothing.
        GetApplication(botMeds._owner).Start(botMeds, meds, bodyPart, timeout, callback, stimCallback);
    }

    public static void OnEndApply(BotAbstractMeds botMeds, bool successful, Action callback)
    {
        L.Debug($"{botMeds.GetType()}: {botMeds.Name()} OnEndApply");
        botMeds.Using = false;
        var medecine = botMeds._owner.Medecine;
        var mind = botMeds._owner.Settings.FileSettings.Mind;
        var nextPossibleUseTime = Time.time + mind.HEAL_DELAY_SEC;
        medecine.FirstAid._nextPosibleUseTime = nextPossibleUseTime;
        medecine.SurgicalKit._nextPosibleUseTime = nextPossibleUseTime;
        medecine.Stimulators._nextPosibleUseTime = Mathf.Max(medecine.Stimulators._nextPosibleUseTime, nextPossibleUseTime);
        medecine.FirstAid.RefreshAndFindDamagedPart();
        switch (botMeds)
        {
            case BotFirstAid firstAid:
                if (successful)
                    firstAid.FirstAidApplied();
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
        callback?.Invoke();
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
