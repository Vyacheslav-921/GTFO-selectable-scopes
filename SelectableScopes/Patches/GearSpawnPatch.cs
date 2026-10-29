using Gear;
using HarmonyLib;
using Player;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(PlayerBackpack), nameof(PlayerBackpack.SpawnAndEquipGearAsync))]
internal static class GearSpawnPatch
{
    private static void Prefix(PlayerBackpack __instance, InventorySlot slot, ref GearIDRange gearIDRange)
    {
        if (!Plugin.Ready || __instance == null || !__instance.IsLocal || gearIDRange == null)
        {
            return;
        }

        GearIDRange updated = ScopeSight.WithSight(slot, gearIDRange);
        if (updated != null)
        {
            gearIDRange = updated;
        }
    }
}
