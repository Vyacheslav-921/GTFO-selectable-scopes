using CellMenu;
using Gear;
using HarmonyLib;

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

        GearIDRange updated = ScopeSight.IconRange(__instance, idRange);
        if (updated != null)
        {
            idRange = updated;
        }
    }
}
