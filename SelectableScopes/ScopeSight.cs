using CellMenu;
using Gear;
using Player;

namespace SelectableScopes;

internal static class ScopeSight
{
    private static readonly Dictionary<string, uint> Originals = new Dictionary<string, uint>(StringComparer.Ordinal);

    private static bool Equipping;

    internal static GearIDRange WithSight(InventorySlot slot, GearIDRange source)
    {
        if (!Plugin.Ready || source == null || !IsWeaponSlot(slot))
        {
            return null;
        }

        uint current = source.GetCompID(eGearComponent.SightPart);
        string key = Identity(source);
        if (!Originals.TryGetValue(key, out uint original))
        {
            original = current;
            Originals[key] = original;
        }

        uint wanted = ScopeChoices.TryGetSightId(slot, out uint chosen) ? chosen : original;
        if (current == wanted)
        {
            return null;
        }

        GearIDRange copy = Copy(source);
        if (copy == null)
        {
            source.SetCompID(eGearComponent.SightPart, wanted, false);
            return null;
        }

        copy.SetCompID(eGearComponent.SightPart, wanted, false);
        return copy;
    }

    internal static void ApplySelection(InventorySlot slot)
    {
        RefreshIcons(slot);
        if (Equipping)
        {
            return;
        }

        try
        {
            EquipNow(slot);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning("Could not update the equipped sight: " + ex.Message);
        }
    }

    private static void EquipNow(InventorySlot slot)
    {
        BackpackItem item = PlayerBackpackManager.GetLocalItem(slot);
        GearIDRange source = item != null ? item.GearIDRange : null;
        if (source == null)
        {
            return;
        }

        GearIDRange updated = WithSight(slot, source);
        if (updated == null)
        {
            return;
        }

        PlayerBackpack backpack = PlayerBackpackManager.LocalBackpack;
        Equipping = true;
        try
        {
            if (backpack != null && backpack.HasGear(updated, slot))
            {
                backpack.TryClearSlot(slot);
            }

            PlayerBackpackManager.EquipLocalGearAsync(slot, updated, null);
            Plugin.LogSource.LogInfo((slot == InventorySlot.GearSpecial ? "Special" : "Primary") + " sight applied to the equipped weapon.");
        }
        finally
        {
            Equipping = false;
        }
    }

    private static void RefreshIcons(InventorySlot slot)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<CM_InventorySlotItem> items;
        try
        {
            items = UnityEngine.Object.FindObjectsOfType<CM_InventorySlotItem>();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Scope icons: " + ex.Message);
            return;
        }

        if (items == null)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            CM_InventorySlotItem item = items[i];
            if (item == null || item.m_slot != slot || item.m_gearID == null || !IsLocal(item))
            {
                continue;
            }

            try
            {
                bool picked = item.IsPicked;
                item.m_lastGearIDChecksum = 0;
                item.LoadData(item.m_gearID, true, item.m_isWeaponPicker);
                item.IsPicked = picked;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug("Scope icon: " + ex.Message);
            }
        }
    }

    private static bool IsLocal(CM_InventorySlotItem item)
    {
        CM_PlayerLobbyBar bar = item.m_parentBar;
        if (bar == null || bar.m_player == null)
        {
            return true;
        }

        return bar.m_player.IsLocal;
    }

    private static GearIDRange Copy(GearIDRange source)
    {
        try
        {
            GearIDRange copy = new GearIDRange(source.GetPacketStruct());
            if (copy != null && copy.GetCompID(eGearComponent.Category) == source.GetCompID(eGearComponent.Category))
            {
                return copy;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Sight copy: " + ex.Message);
        }

        try
        {
            GearIDRange copy = new GearIDRange();
            if (copy.TryApplyCompJSON(source.ToJSON()))
            {
                return copy;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Sight json copy: " + ex.Message);
        }

        return null;
    }

    private static bool IsWeaponSlot(InventorySlot slot)
    {
        return slot == InventorySlot.GearStandard || slot == InventorySlot.GearSpecial;
    }

    private static string Identity(GearIDRange range)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(160);
        builder.Append(range.PublicGearName);
        builder.Append('|');
        for (int component = 1; component <= (int)eGearComponent.PlatformPerkCID; component++)
        {
            if (component == (int)eGearComponent.SightPart)
            {
                continue;
            }

            builder.Append(range.GetCompID((eGearComponent)component));
            builder.Append('.');
        }

        return builder.ToString();
    }
}
