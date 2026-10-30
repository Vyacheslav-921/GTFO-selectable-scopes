using Gear;
using HarmonyLib;
using Player;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(PlayerBackpack), "SetupAmmoStorageForItem")]
internal static class ClipKeepPatch
{
    private static void Prefix(PlayerBackpack __instance, InventorySlot slot)
    {
        if (__instance == null || !__instance.IsLocal)
        {
            return;
        }

        ScopeSight.StageClip(slot);
    }

    private static void Postfix(PlayerBackpack __instance, InventorySlot slot)
    {
        if (__instance == null || !__instance.IsLocal)
        {
            return;
        }

        ScopeSight.RestoreClip(slot);
    }
}

[HarmonyPatch(typeof(BulletWeapon), nameof(BulletWeapon.OnWield))]
internal static class ClipWieldPatch
{
    private static void Prefix(BulletWeapon __instance)
    {
        ScopeSight.StageWeapon(__instance);
    }

    private static void Postfix(BulletWeapon __instance)
    {
        ScopeSight.RestoreWeapon(__instance);
    }
}

[HarmonyPatch(typeof(BulletWeapon), nameof(BulletWeapon.OnGearSpawnComplete))]
internal static class ClipSpawnedPatch
{
    private static void Prefix(BulletWeapon __instance)
    {
        ScopeSight.StageWeapon(__instance);
    }

    private static void Postfix(BulletWeapon __instance)
    {
        ScopeSight.RestoreWeapon(__instance);
    }
}

[HarmonyPatch(typeof(BulletWeapon), "Update")]
internal static class ClipUpdatePatch
{
    private static void Postfix(BulletWeapon __instance)
    {
        ScopeSight.AfterWeaponUpdate(__instance);
    }
}

[HarmonyPatch(typeof(PlayerAmmoStorage), "SetClipAmmoInSlot", typeof(InventorySlot))]
internal static class ClipStorePatch
{
    private static void Postfix(PlayerAmmoStorage __instance, InventorySlot slot)
    {
        ScopeSight.StageStored(__instance, slot);
    }
}

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
