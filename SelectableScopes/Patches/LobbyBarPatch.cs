using CellMenu;
using HarmonyLib;
using SNetwork;

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

[HarmonyPatch(typeof(CM_PlayerLobbyBar), "Update")]
internal static class LobbyBarLifetimePatch
{
    private static void Postfix()
    {
        ScopeLoadoutCategory.ReleaseDestroyed();
        ScopeLoadoutCategory.AlignOpenSlot();
    }
}

[HarmonyPatch(typeof(FocusStateManager), nameof(FocusStateManager.ToggleMenu))]
internal static class MenuTogglePatch
{
    private static void Postfix()
    {
        ScopeLoadoutCategory.NoteMenuFocus();
    }
}

[HarmonyPatch(typeof(FocusStateManager), nameof(FocusStateManager.EnterMenu))]
internal static class MenuEnterPatch
{
    private static void Postfix()
    {
        ScopeLoadoutCategory.NoteMenuFocus();
    }
}

[HarmonyPatch(typeof(FocusStateManager), nameof(FocusStateManager.ExitMenu))]
internal static class MenuExitPatch
{
    private static void Postfix()
    {
        ScopeLoadoutCategory.NoteMenuFocus();
    }
}

[HarmonyPatch(typeof(CM_InventorySlotItem), nameof(CM_InventorySlotItem.PickWeapon))]
internal static class WeaponPickedPatch
{
    private static void Postfix(CM_InventorySlotItem __instance, SNet_Player player)
    {
        if (!Plugin.Ready || __instance == null || __instance.m_gearID == null)
        {
            return;
        }

        if (player != null && !player.IsLocal)
        {
            return;
        }

        if (player == null)
        {
            CM_PlayerLobbyBar bar = __instance.m_parentBar;
            if (bar != null && bar.m_player != null && !bar.m_player.IsLocal)
            {
                return;
            }
        }

        ScopeSight.RequestSelectedIcon(__instance.m_slot, __instance.m_gearID);
    }
}
