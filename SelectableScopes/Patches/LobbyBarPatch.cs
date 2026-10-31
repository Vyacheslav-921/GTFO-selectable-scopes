using CellMenu;
using HarmonyLib;
using Player;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.SetupFromPage))]
internal static class LobbyBarPatch
{
    [HarmonyPriority(Priority.Last)]
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
        if (__instance != null && __instance.m_player != null && __instance.m_player.IsLocal)
        {
            ScopeSight.RequestWeaponIcons();
        }
    }
}

[HarmonyPatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.ShowWeaponSelectionPopup))]
internal static class WeaponPopupPatch
{
    private static void Prefix(CM_PlayerLobbyBar __instance, InventorySlot slot)
    {
        if (__instance == null || __instance.m_player == null || !__instance.m_player.IsLocal)
        {
            return;
        }

        ScopeSight.RequestWeaponIcons(slot);
    }
}
