using CellMenu;
using HarmonyLib;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(CM_PageLoadout), "UpdatePageData")]
internal static class LoadoutPagePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(800)]
    private static void BeforeLoadoutRefresh()
    {
        ScopeApplier.ApplyConfigured();
    }

    [HarmonyPostfix]
    [HarmonyPriority(0)]
    private static void AfterLoadoutRefresh()
    {
        ScopeApplier.ApplyConfigured();
    }
}
