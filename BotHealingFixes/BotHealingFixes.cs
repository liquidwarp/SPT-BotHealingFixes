using BepInEx;
using BotHealingFixes.Utils;
using SPT.Reflection.Patching;
#if DEBUG
using BotHealingFixes.Brain;
using DrakiaXYZ.BigBrain.Brains;
#endif

namespace BotHealingFixes;

[BepInPlugin(MyModInfo.MOD_GUID, MyModInfo.MOD_NAME, MyModInfo.MOD_VERSION)]
#if DEBUG
[BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
#endif
public class BotHealingFixes : BaseUnityPlugin
{
    private PatchManager _patchManager;
    private float _nextMedicalUpdate;

    public void Awake()
    {
        L.SetLogger(Logger);
        L.Info($"Version {MyModInfo.MOD_FULL_VERSION}");

        _patchManager = new PatchManager(this, true);
        _patchManager.EnablePatches();

#if DEBUG
        BrainManager.AddCustomLayer(typeof(DebugHealLayer), ["PMC", "PmcUsec", "PmcBear", "Assault", "CursAssault"], int.MaxValue);
#endif
    }

    private void Update()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now < _nextMedicalUpdate)
            return;
        _nextMedicalUpdate = now + 0.1f;
        MedUtils.UpdateApplications(now);
    }

#if DEBUG
    public void OnDestroy()
    {
        _patchManager?.DisablePatches();
        BrainManager.RemoveLayer("DebugHealLayer", ["PMC", "PmcUsec", "PmcBear", "Assault", "CursAssault"]);
    }
#endif
}
