using EFT.HealthSystem;
using EFT.InventoryLogic;

namespace BotHealingFixes.Utils;

public static class MedUtils
{
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
