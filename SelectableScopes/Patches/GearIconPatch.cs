using CellMenu;
using Gear;
using HarmonyLib;
using Player;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(CM_InventorySlotItem), nameof(CM_InventorySlotItem.LoadData))]
internal static class GearIconPatch
{
    private static void Prefix(CM_InventorySlotItem __instance, ref GearIDRange idRange)
    {
        if (!Plugin.Ready || __instance == null || idRange == null)
        {
            return;
        }

        CM_PlayerLobbyBar bar = __instance.m_parentBar;
        if (bar != null && bar.m_player != null && !bar.m_player.IsLocal)
        {
            return;
        }

        GearIDRange updated = ScopeSight.WithSight(__instance.m_slot, idRange);
        if (updated != null)
        {
            idRange = updated;
        }
    }
}
