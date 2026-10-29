using CellMenu;
using HarmonyLib;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.SetupFromPage))]
internal static class LobbyBarPatch
{
    private static void Postfix(CM_PlayerLobbyBar __instance)
    {
        ScopeLoadoutCategory.EnsureButton(__instance);
    }
}

[HarmonyPatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.PrepareForIntro))]
internal static class LobbyBarIntroPatch
{
    private static void Postfix(CM_PlayerLobbyBar __instance)
    {
        ScopeLoadoutCategory.HideButton(__instance);
    }
}

[HarmonyPatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.SpawnPlayerModel))]
internal static class LobbyBarSpawnPatch
{
    private static void Postfix(CM_PlayerLobbyBar __instance)
    {
        ScopeLoadoutCategory.ShowButton(__instance);
    }
}
