# Modifications of ozen's Bot Healing Fixes

Fork of Bot Healing Fixes. The forked SAIN requires this version.

## Fixes

- Fix bots stopping bleeds with medkits, or with items that can't afford the bleed, instead of tourniquets and bandages. Medkit HP now goes to healing and surgical kit uses to surgery. Heavy bleeds are treated before light ones.
- Fix bots using up a medkit on a bleed it can't afford to stop, instead of healing with it.
- Fix bots doing surgery with items that can't afford it, such as medkits that mods give a surgery effect. Dedicated surgical kits come first.
- Fix bots holding on to a used-up med item instead of switching or picking nothing.
- Fix bots reacting late to a new bleed or a destroyed limb. They pick the right item and body part as soon as it happens, not at the game's next check.
- Fix bots injecting whichever stim comes first, and getting stuck unable to use stims again if one fails to go in hand. They pick one for their needs: a bleed-stopping stim when bleeding, a healing stim when hurt, and keep those for when they're needed. They never inject stims that drain HP (M.U.L.E, SJ9, Obdolbos...); those bring questionable combat benefits anyway.
- Bots now use stims as a last resort when their med items can't cover their needs: a bleed-stopping stim (Zagustin, AHF1-M, Perfotoran) for a bleed nothing else can treat, a healing stim (eTG-c, Propital...) when they have no medkit left.
- Fix bots being unable to heal again after an interrupted or timed-out heal. They keep control of their hands until the item is put away, then can heal again.
- Fix bots sometimes not drawing a weapon after healing. They fall back to their main weapon if the previous one isn't available.
- Fix cancelled heals leaving the medical item running instead of putting it away.
- Healing no longer allocates memory per treatment, which matters with garbage collection disabled in raid.
- All canges compatible with SAIN, and ORBIT and its BotHealingFixes compatibility.
