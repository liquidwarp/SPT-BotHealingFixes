#if DEBUG
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;

namespace BotHealingFixes.Brain;

public class DebugHealLogic(BotOwner botOwner) : CustomLogic(botOwner)
{
    private readonly HealNode _healNode = new(botOwner);

    public override void Start()
    {
        BotOwner.Mover.Stop();
    }

    public override void Update(CustomLayer.ActionData data)
    {
        _healNode.UpdateNodeByBrain(data);
    }

    public void ApplyDamage(float damage, EBodyPart part)
    {
        ((ActiveHealthController)BotOwner.HealthController).ApplyDamage(part, damage, default);
    }

    public void DoBleed(bool isHeavy, EBodyPart part)
    {
        ((ActiveHealthController)BotOwner.HealthController).DoBleed(isHeavy, part);
    }
}

#endif
