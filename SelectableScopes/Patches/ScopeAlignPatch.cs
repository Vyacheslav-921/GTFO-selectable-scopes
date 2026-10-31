using Gear;
using HarmonyLib;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(GearPartHolder), "Update")]
internal static class ScopeModelAlignPatch
{
    private static bool Logged;

    private static void Postfix(GearPartHolder __instance)
    {
        try
        {
            ScopeSight.MaintainClips();
            ScopeSight.MaintainIconRefresh();
            ScopeLoadoutCategory.ReleaseDestroyed();
            ScopeAlignment.ApplyScope(__instance);
        }
        catch (Exception ex)
        {
            if (!Logged)
            {
                Logged = true;
                Plugin.LogSource.LogWarning("Could not place the scope: " + ex.Message);
            }
        }
    }
}

[HarmonyPatch(typeof(GearPartHolder), nameof(GearPartHolder.OnAllPartsSpawned))]
internal static class ScopeModelSpawnPatch
{
    private static void Postfix(GearPartHolder __instance)
    {
        try
        {
            ScopeAlignment.ApplyScope(__instance);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Scope placement after spawn: " + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(GearPartHolder), "OnDestroy")]
internal static class ScopeModelForgetPatch
{
    private static void Postfix(GearPartHolder __instance)
    {
        ScopeAlignment.Forget(__instance);
    }
}
