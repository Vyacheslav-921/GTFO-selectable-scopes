using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using GTFO.API;
using HarmonyLib;
using SelectableScopes.Patches;

namespace SelectableScopes;

[BepInPlugin(Guid, "Selectable Scopes", Version)]
[BepInDependency("dev.gtfomodding.gtfo-api", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "SelectableScopes";

    public const string Version = "1.0.0";

    internal static Plugin Instance { get; private set; }

    internal static ManualLogSource LogSource { get; private set; }

    internal static ConfigEntry<bool> Enabled { get; private set; }

    internal static bool Ready { get; set; }

    public override void Load()
    {
        Instance = this;
        LogSource = Log;
        Enabled = Config.Bind("General", "Enabled", true, "When true, each weapon section below can replace that weapon's sight. Default leaves the weapon unchanged.");
        Harmony harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(LoadoutPagePatch));
        harmony.PatchAll(typeof(LobbyBarPatch));
        harmony.PatchAll(typeof(LobbyBarIntroPatch));
        harmony.PatchAll(typeof(LobbyBarSpawnPatch));
        harmony.PatchAll(typeof(WeaponPickedPatch));
        harmony.PatchAll(typeof(LobbyBarLifetimePatch));
        harmony.PatchAll(typeof(MenuTogglePatch));
        harmony.PatchAll(typeof(MenuEnterPatch));
        harmony.PatchAll(typeof(MenuExitPatch));
        harmony.PatchAll(typeof(GearSpawnPatch));
        harmony.PatchAll(typeof(ClipKeepPatch));
        harmony.PatchAll(typeof(ClipWieldPatch));
        harmony.PatchAll(typeof(ClipSpawnedPatch));
        harmony.PatchAll(typeof(ClipUpdatePatch));
        harmony.PatchAll(typeof(ClipStorePatch));
        harmony.PatchAll(typeof(GearIconPatch));
        harmony.PatchAll(typeof(ScopeModelAlignPatch));
        harmony.PatchAll(typeof(ScopeModelSpawnPatch));
        harmony.PatchAll(typeof(ScopeModelForgetPatch));
        harmony.PatchAll(typeof(ScopeCrosshairPreparePatch));
        harmony.PatchAll(typeof(ScopeCrosshairKeepPatch));
        harmony.PatchAll(typeof(ScopeCrosshairLayerPatch));
        EventAPI.OnAssetsLoaded += ScopeCatalog.BuildAndApply;
        Log.LogInfo("Selectable Scopes loaded. Weapon entries are written after game data loads.");
    }
}
