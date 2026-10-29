using Gear;
using HarmonyLib;
using Player;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(PlayerBackpack), nameof(PlayerBackpack.SpawnAndEquipGearAsync))]
internal static class GearSpawnPatch
{
    private static void Prefix(PlayerBackpack __instance, InventorySlot slot, GearIDRange gearIDRange)
    {
        if (!Plugin.Ready || __instance == null || !__instance.IsLocal || gearIDRange == null)
        {
            return;
        }

        if (!ScopeChoices.TryGetSightId(slot, out uint sightId))
        {
            return;
        }

        gearIDRange.SetCompID(eGearComponent.SightPart, sightId, true);
    }
}
