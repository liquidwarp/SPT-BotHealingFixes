using BepInEx;
using BotHealingFixes.Utils;
using SPT.Reflection.Patching;
#if DEBUG
using BotHealingFixes.Brain;
using DrakiaXYZ.BigBrain.Brains;
#endif

namespace BotHealingFixes;

[BepInPlugin("com.ozen.bothealingfixes", "Bot Healing Fixes", "1.0.0")]
#if DEBUG
[BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
#endif
public class BotHealingFixes : BaseUnityPlugin
{
    private PatchManager _patchManager;

    public void Awake()
    {
        L.SetLogger(Logger);

        _patchManager = new PatchManager(this, true);
        _patchManager.EnablePatches();

#if DEBUG
        BrainManager.AddCustomLayer(typeof(DebugHealLayer), ["PMC", "PmcUsec", "PmcBear", "Assault", "CursAssault"], int.MaxValue);
#endif
    }

#if DEBUG
    public void OnDestroy()
    {
        _patchManager?.DisablePatches();
        BrainManager.RemoveLayer("DebugHealLayer", ["PMC", "PmcUsec", "PmcBear", "Assault", "CursAssault"]);
    }
#endif
}
