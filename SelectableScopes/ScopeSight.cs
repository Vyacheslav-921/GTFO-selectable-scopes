using CellMenu;
using Gear;
using Player;
using UnityEngine;

namespace SelectableScopes;

internal static class ScopeSight
{
    private static readonly Dictionary<string, uint> Originals = new Dictionary<string, uint>(StringComparer.Ordinal);

    private static readonly Dictionary<string, GearIDRange> Prepared = new Dictionary<string, GearIDRange>(StringComparer.Ordinal);

    private static readonly Dictionary<InventorySlot, HeldClip> PendingClips = new Dictionary<InventorySlot, HeldClip>();

    private static readonly HashSet<uint> PostedIcons = new HashSet<uint>();

    private static int MaintainedFrame = -1;

    private static int IconFrame = -1;

    private static int IconWait;

    private static bool IconsPending;

    private static bool IconsRetry;

    private static int IconRetries;

    private static int IconQuiet;

    private static bool IconsAwaitJob;

    private static bool IconSawJob;

    private static bool Equipping;

    internal static GearIDRange WithSight(InventorySlot slot, GearIDRange source)
    {
        if (!Plugin.Ready || source == null || !IsWeaponSlot(slot) || !ScopeCatalog.AppliesTo(source))
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
        return WithSightChange(source, wanted);
    }

    internal static GearIDRange IconRange(CM_InventorySlotItem item, GearIDRange source)
    {
        if (item == null || source == null || !IsWeaponSlot(item.m_slot) || !IsLocal(item))
        {
            return null;
        }

        return SightForIcon(item.m_slot, source);
    }

    internal static void RequestWeaponIcons()
    {
        RequestWeaponIcons(InventorySlot.GearStandard);
        RequestWeaponIcons(InventorySlot.GearSpecial);
    }

    internal static void RequestWeaponIcons(InventorySlot slot)
    {
        if (!Plugin.Ready || !IsWeaponSlot(slot))
        {
            return;
        }

        int posted = PostMissingIcons(slot);
        if (posted < 0)
        {
            IconRetries++;
            if (IconRetries <= 40)
            {
                IconsRetry = true;
                IconsPending = true;
                IconWait = 0;
            }

            return;
        }

        IconRetries = 0;

        if (posted > 0)
        {
            IconsAwaitJob = true;
            IconSawJob = false;
            Plugin.LogSource.LogInfo("Rendering " + posted + " weapon icon" + (posted == 1 ? "" : "s") + " for the current sight.");
        }

        IconsPending = true;
        IconWait = 0;
    }

    internal static void MaintainIconRefresh()
    {
        if (!IconsPending && !IconsRetry)
        {
            return;
        }

        int frame = Time.frameCount;
        if (frame == IconFrame)
        {
            return;
        }

        IconFrame = frame;
        IconWait++;
        if (IconsRetry && IconWait % 30 == 0)
        {
            IconsRetry = false;
            RequestWeaponIcons();
        }

        if (IconRendererBusy() && IconWait < 1800)
        {
            return;
        }

        if (IconWait < 8)
        {
            return;
        }

        IconsPending = false;
        IconsAwaitJob = false;
        IconSawJob = false;
        RefreshIcons(InventorySlot.GearStandard);
        RefreshIcons(InventorySlot.GearSpecial);
    }

    internal static void ApplySelection(InventorySlot slot)
    {
        BackpackItem equipped = PlayerBackpackManager.GetLocalItem(slot);
        if (equipped != null && equipped.GearIDRange != null && !ScopeCatalog.AppliesTo(equipped.GearIDRange))
        {
            Plugin.LogSource.LogInfo("Left the revolver on its default sight.");
            return;
        }

        RememberClip(slot);
        if (GameStateManager.CurrentStateName != eGameStateName.InLevel)
        {
            RequestWeaponIcons(slot);
            RefreshIcons(slot);
        }
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
            RevertPack(slot);
            PendingClips.Remove(slot);
            return;
        }

        PlayerBackpack backpack = PlayerBackpackManager.LocalBackpack;
        ItemEquippable previous = item.Instance != null ? item.Instance.TryCast<ItemEquippable>() : null;
        GameObject previousObject = previous != null ? previous.gameObject : null;
        Equipping = true;
        try
        {
            if (backpack != null)
            {
                backpack.TryClearSlot(slot);
            }

            if (previousObject != null)
            {
                UnityEngine.Object.Destroy(previousObject);
            }

            Stage(slot);
            PlayerBackpackManager.EquipLocalGearAsync(slot, updated, null);
            Plugin.LogSource.LogInfo((slot == InventorySlot.GearSpecial ? "Special" : "Primary") + " sight applied to the equipped weapon.");
        }
        finally
        {
            Equipping = false;
        }
    }

    internal static void MaintainClips()
    {
        if (PendingClips.Count == 0)
        {
            return;
        }

        int frame = Time.frameCount;
        if (frame == MaintainedFrame)
        {
            return;
        }

        MaintainedFrame = frame;
        List<InventorySlot> slots = new List<InventorySlot>(PendingClips.Keys);
        for (int i = 0; i < slots.Count; i++)
        {
            Enforce(slots[i], true);
        }
    }

    internal static void RestoreClip(InventorySlot slot)
    {
        Enforce(slot, false);
    }

    internal static void StageClip(InventorySlot slot)
    {
        Stage(slot);
    }

    internal static void StageStored(PlayerAmmoStorage storage, InventorySlot slot)
    {
        if (storage == null || storage.m_playerBackpack == null || !storage.m_playerBackpack.IsLocal)
        {
            return;
        }

        Stage(slot);
    }

    internal static void StageWeapon(ItemEquippable weapon)
    {
        if (!IsLocalOwned(weapon) || !TryPendingSlot(weapon, out InventorySlot slot))
        {
            return;
        }

        Stage(slot);
    }

    internal static void RestoreWeapon(ItemEquippable weapon)
    {
        if (!IsLocalOwned(weapon) || !TryPendingSlot(weapon, out InventorySlot slot))
        {
            return;
        }

        if (!PendingClips.TryGetValue(slot, out HeldClip held))
        {
            return;
        }

        WriteClip(slot, weapon, TargetCount(held, weapon));
    }

    internal static void AfterWeaponUpdate(ItemEquippable weapon)
    {
        if (!IsLocalOwned(weapon) || !TryPendingSlot(weapon, out InventorySlot slot))
        {
            return;
        }

        if (!PendingClips.TryGetValue(slot, out HeldClip held))
        {
            return;
        }

        int target = TargetCount(held, weapon);
        if (ReadClip(weapon) == target)
        {
            return;
        }

        held.Stable = 0;
        PendingClips[slot] = held;
        WriteClip(slot, weapon, target);
    }

    private static void Enforce(InventorySlot slot, bool count)
    {
        if (!PendingClips.TryGetValue(slot, out HeldClip held))
        {
            return;
        }

        BackpackItem current = PlayerBackpackManager.GetLocalItem(slot);
        ItemEquippable weapon = current != null && current.Instance != null ? current.Instance.TryCast<ItemEquippable>() : null;
        if (weapon == null)
        {
            Stage(slot);
            if (!count)
            {
                return;
            }

            held.Missing++;
            if (held.Missing > 600)
            {
                PendingClips.Remove(slot);
                return;
            }

            PendingClips[slot] = held;
            return;
        }

        if (held.Receiver != 0 && current.GearIDRange != null)
        {
            uint receiver = 0;
            try
            {
                receiver = current.GearIDRange.GetCompID(eGearComponent.ReceiverPart);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug("Clip weapon: " + ex.Message);
            }

            if (receiver != 0 && receiver != held.Receiver)
            {
                RevertPack(slot);
                PendingClips.Remove(slot);
                return;
            }
        }

        int target = TargetCount(held, weapon);
        if (ReadClip(weapon) == target)
        {
            if (!count)
            {
                return;
            }

            held.Stable++;
            if (held.Stable >= 3)
            {
                PendingClips.Remove(slot);
                Plugin.LogSource.LogInfo("Magazine kept at " + target + " after the sight change.");
                return;
            }

            PendingClips[slot] = held;
            return;
        }

        if (!count)
        {
            WriteClip(slot, weapon, target);
            return;
        }

        held.Stable = 0;
        held.Corrections++;
        if (held.Corrections > 90)
        {
            PendingClips.Remove(slot);
            Plugin.LogSource.LogWarning("Magazine stayed at " + ReadClip(weapon) + " after a sight change. Left it there so firing still spends rounds.");
            return;
        }

        PendingClips[slot] = held;
        WriteClip(slot, weapon, target);
    }

    private static void RememberClip(InventorySlot slot)
    {
        BackpackItem item = PlayerBackpackManager.GetLocalItem(slot);
        ItemEquippable weapon = item != null && item.Instance != null ? item.Instance.TryCast<ItemEquippable>() : null;
        if (weapon == null)
        {
            return;
        }

        int clip;
        int max;
        try
        {
            clip = weapon.GetCurrentClip();
            max = weapon.GetMaxClip();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip snapshot: " + ex.Message);
            return;
        }

        if (clip <= 0 && PendingClips.ContainsKey(slot))
        {
            return;
        }

        uint receiver = 0;
        try
        {
            if (item.GearIDRange != null)
            {
                receiver = item.GearIDRange.GetCompID(eGearComponent.ReceiverPart);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip weapon: " + ex.Message);
        }

        float original = 0f;
        bool hasPack = false;
        PlayerBackpack backpack = PlayerBackpackManager.LocalBackpack;
        if (backpack != null && backpack.AmmoStorage != null)
        {
            try
            {
                InventorySlotAmmo ammo = backpack.AmmoStorage.GetInventorySlotAmmo(slot);
                if (ammo != null)
                {
                    original = ammo.AmmoInPack;
                    hasPack = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug("Clip store: " + ex.Message);
            }
        }

        PendingClips[slot] = new HeldClip
        {
            Clip = Math.Max(0, clip),
            Max = Math.Max(0, max),
            Receiver = receiver,
            PackFloor = Mathf.Floor(original + 0.0001f),
            PackOriginal = original,
            HasPack = hasPack
        };

        Stage(slot);
    }

    private static void Stage(InventorySlot slot)
    {
        if (!PendingClips.TryGetValue(slot, out HeldClip held) || !held.HasPack)
        {
            return;
        }

        PlayerBackpack backpack = PlayerBackpackManager.LocalBackpack;
        if (backpack == null || backpack.AmmoStorage == null)
        {
            return;
        }

        int max = held.Max > 0 ? held.Max : held.Clip;
        try
        {
            InventorySlotAmmo ammo = backpack.AmmoStorage.GetInventorySlotAmmo(slot);
            if (ammo == null)
            {
                return;
            }

            ammo.AmmoInPack = held.PackFloor + StoredRel(held.Clip, max);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip stage: " + ex.Message);
        }
    }

    private static void RevertPack(InventorySlot slot)
    {
        if (!PendingClips.TryGetValue(slot, out HeldClip held) || !held.HasPack)
        {
            return;
        }

        PlayerBackpack backpack = PlayerBackpackManager.LocalBackpack;
        if (backpack == null || backpack.AmmoStorage == null)
        {
            return;
        }

        try
        {
            InventorySlotAmmo ammo = backpack.AmmoStorage.GetInventorySlotAmmo(slot);
            if (ammo != null)
            {
                ammo.AmmoInPack = held.PackOriginal;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip revert: " + ex.Message);
        }
    }

    private static void WriteClip(InventorySlot slot, ItemEquippable weapon, int target)
    {
        Stage(slot);
        try
        {
            weapon.SetCurrentClip(target);
            BulletWeapon gun = weapon.TryCast<BulletWeapon>();
            if (gun != null)
            {
                gun.m_hasGottenClip = true;
                gun.UpdateMagazineCartridgesVisible(true);
            }

            PlayerBackpack backpack = PlayerBackpackManager.LocalBackpack;
            if (backpack != null && backpack.AmmoStorage != null)
            {
                backpack.AmmoStorage.UpdateSlotAmmoUI(slot);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip restore: " + ex.Message);
        }

        Stage(slot);
    }

    private static int TargetCount(HeldClip held, ItemEquippable weapon)
    {
        int max = held.Max;
        try
        {
            int live = weapon.GetMaxClip();
            if (live > 0)
            {
                max = live;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip max: " + ex.Message);
        }

        if (max > 0 && held.Clip > max)
        {
            return max;
        }

        return held.Clip;
    }

    private static int ReadClip(ItemEquippable weapon)
    {
        try
        {
            return weapon.GetCurrentClip();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip read: " + ex.Message);
            return -1;
        }
    }

    private static float StoredRel(int clip, int max)
    {
        if (clip <= 0 || max <= 0)
        {
            return 0f;
        }

        float rel = clip >= max ? (max - 0.45f) / max : clip / (float)max;
        if (rel >= 1f)
        {
            rel = 0.999f;
        }

        return rel < 0f ? 0f : rel;
    }

    private static bool TryPendingSlot(ItemEquippable weapon, out InventorySlot slot)
    {
        slot = default;
        if (weapon == null)
        {
            return false;
        }

        try
        {
            if (weapon.ItemDataBlock == null)
            {
                return false;
            }

            slot = weapon.ItemDataBlock.inventorySlot;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip slot: " + ex.Message);
            return false;
        }

        return PendingClips.ContainsKey(slot);
    }

    private static bool IsLocalOwned(ItemEquippable weapon)
    {
        if (weapon == null)
        {
            return false;
        }

        try
        {
            PlayerAgent agent = weapon.Owner;
            return agent == null || agent.IsLocallyOwned;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Clip owner: " + ex.Message);
            return false;
        }
    }

    private static GearIDRange SightForIcon(InventorySlot slot, GearIDRange source)
    {
        if (!ScopeChoices.TryGetSightId(slot, out _))
        {
            return null;
        }

        return WithSight(slot, source);
    }

    private static int PostMissingIcons(InventorySlot slot)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GearIDRange> gears;
        try
        {
            gears = GearManager.GetAllGearForSlot(slot);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Weapon icon list: " + ex.Message);
            return -1;
        }

        if (gears == null || gears.Count == 0)
        {
            return -1;
        }

        int posted = 0;
        for (int i = 0; i < gears.Count; i++)
        {
            GearIDRange source = gears[i];
            if (source == null)
            {
                continue;
            }

            GearIDRange display = SightForIcon(slot, source) ?? source;
            uint checksum;
            try
            {
                checksum = display.GetChecksum();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug("Weapon icon id: " + ex.Message);
                continue;
            }

            if (!PostedIcons.Add(checksum))
            {
                continue;
            }

            if (HasIcon(checksum) && display == source)
            {
                continue;
            }

            try
            {
                GearManager.PostIconJobs(display);
                posted++;
            }
            catch (Exception ex)
            {
                PostedIcons.Remove(checksum);
                Plugin.LogSource.LogDebug("Weapon icon job: " + ex.Message);
            }
        }

        return posted;
    }

    private static bool HasIcon(uint checksum)
    {
        try
        {
            return GearIconRendering.TryGetGearIconSprite(checksum, out Sprite sprite) && sprite != null;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Weapon icon lookup: " + ex.Message);
            return false;
        }
    }

    private static bool IconRendererBusy()
    {
        try
        {
            GearIconRendering rendering = GearIconRendering.Current;
            if (rendering == null || rendering.m_currentJob != null)
            {
                if (rendering != null && rendering.m_currentJob != null)
                {
                    IconSawJob = true;
                }

                IconQuiet = 0;
                return true;
            }

            if (IconsAwaitJob && !IconSawJob && IconWait < 120)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Weapon icon queue: " + ex.Message);
            return false;
        }

        IconQuiet++;
        return IconQuiet < 45;
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

    private static GearIDRange WithSightChange(GearIDRange source, uint sight)
    {
        if (source.GetCompID(eGearComponent.SightPart) == sight)
        {
            return null;
        }

        string key = Identity(source) + "|" + sight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!Prepared.TryGetValue(key, out GearIDRange template) || template == null)
        {
            template = Copy(source);
            if (template == null)
            {
                return null;
            }

            template.SetCompID(eGearComponent.SightPart, sight, false);
            ClearPlayfab(template);
            Prepared[key] = template;
        }

        GearIDRange returned = Copy(template);
        if (returned == null)
        {
            return null;
        }

        returned.SetCompID(eGearComponent.SightPart, sight, false);
        CarryIdentity(source, returned);
        return returned;
    }

    private static void ClearPlayfab(GearIDRange range)
    {
        range.PlayfabItemInstanceId = null;
        range.PlayfabItemId = null;
        range.PlayfabItemName = null;
    }

    private static void CarryIdentity(GearIDRange source, GearIDRange copy)
    {
        copy.PlayfabItemInstanceId = source.PlayfabItemInstanceId;
        copy.PlayfabItemId = source.PlayfabItemId;
        copy.PlayfabItemName = source.PlayfabItemName;
        copy.OfflineGearType = source.OfflineGearType;
        if (!string.IsNullOrEmpty(source.PublicGearName))
        {
            copy.PublicGearName = source.PublicGearName;
        }
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

    private struct HeldClip
    {
        public int Clip;

        public int Max;

        public uint Receiver;

        public float PackFloor;

        public float PackOriginal;

        public bool HasPack;

        public int Stable;

        public int Missing;

        public int Corrections;
    }
}
