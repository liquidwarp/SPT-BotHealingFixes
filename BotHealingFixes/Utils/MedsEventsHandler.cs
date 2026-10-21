using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;

namespace BotHealingFixes.Utils;

/// The game only re-picks med items every 20s and after using one, and only re-checks
/// destroyed parts when hit and after surgery. React to the health changes that decide the right pick instead, so a new bleed is
/// treated with a suitable item right away, and IsBleeding and the part to operate on aren't stale in between.
/// While an item is being used, its end of use refreshes both instead.
public sealed class MedsEventsHandler
{
    private static readonly ConditionalWeakTable<BotMedecine, MedsEventsHandler> _handlers = new();

    private readonly BotMedecine _medecine;
    private readonly IHealthController _health;

    private MedsEventsHandler(BotMedecine medecine, IHealthController health)
    {
        _medecine = medecine;
        _health = health;
    }

    public static void Subscribe(BotMedecine medecine)
    {
        var health = medecine._owner.GetPlayer.HealthController;
        if (health == null || _handlers.TryGetValue(medecine, out _))
        {
            return;
        }

        var handler = new MedsEventsHandler(medecine, health);
        health.EffectAddedEvent += handler.OnEffectChanged;
        health.EffectRemovedEvent += handler.OnEffectChanged;
        health.BodyPartDestroyedEvent += handler.OnBodyPartDestroyed;
        health.BodyPartRestoredEvent += handler.OnBodyPartRestored;
        _handlers.Add(medecine, handler);
    }

    public static void Unsubscribe(BotMedecine medecine)
    {
        if (!_handlers.TryGetValue(medecine, out var handler))
        {
            return;
        }

        var health = handler._health;
        health.EffectAddedEvent -= handler.OnEffectChanged;
        health.EffectRemovedEvent -= handler.OnEffectChanged;
        health.BodyPartDestroyedEvent -= handler.OnBodyPartDestroyed;
        health.BodyPartRestoredEvent -= handler.OnBodyPartRestored;
        _handlers.Remove(medecine);
    }

    private void OnEffectChanged(IHealthEffect effect)
    {
        var firstAid = _medecine.FirstAid;
        if (effect is not IBleeding || firstAid.Using)
        {
            return;
        }
        firstAid.RefreshMeds();
        firstAid.FindDamagedPart();
    }

    private void OnBodyPartDestroyed(EBodyPart bodyPart, EDamageType damageType)
    {
        OnBodyPartChanged();
    }

    private void OnBodyPartRestored(EBodyPart bodyPart, ValueStruct health)
    {
        OnBodyPartChanged();
    }

    private void OnBodyPartChanged()
    {
        var surgicalKit = _medecine.SurgicalKit;
        if (!surgicalKit.Using)
        {
            surgicalKit.FindDamagedPart();
        }
    }
}
