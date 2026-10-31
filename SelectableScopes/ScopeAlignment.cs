using System.Globalization;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using CellMenu;
using GameData;
using Gear;
using Player;
using UnityEngine;

namespace SelectableScopes;

internal static class ScopeAlignment
{
    internal const float Step = 0.005f;

    private const float Max = 0.25f;

    private const string Section = "Alignment";

    private const string ScopeSuffix = " Scope Vertical";

    private const string ForwardSuffix = " Scope Forward";

    private const string RetiredHandsSuffix = " Aim Hands Vertical";

    private const string CarrierName = "ScopeAlignCarrier";

    // Stored positive is up. These part roots point the other way along local Y.
    private const float VisualSign = -1f;

    // Stored positive is forward, toward the muzzle. Sight local +Z points that way.
    private const float ForwardSign = 1f;

    // Added to every weapon's default forward alignment for this sight.
    private static readonly Dictionary<string, float> SightForwardShift = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
    {
        { "Sight_5", 0.1f }
    };

    private static readonly Dictionary<string, string> Stems = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> SightsByStem = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly Dictionary<(InventorySlot Slot, string Weapon, string Sight), Pair> Saved = new Dictionary<(InventorySlot, string, string), Pair>();

    private static readonly Dictionary<(InventorySlot Slot, string Weapon, string Sight), Vector2> Session = new Dictionary<(InventorySlot, string, string), Vector2>();

    private static readonly Dictionary<uint, string> NamesById = new Dictionary<uint, string>();

    private static readonly Dictionary<long, PlaceState> Places = new Dictionary<long, PlaceState>();

    // Front irons are a bare mesh. The rear half owns Sight_Align, and the two were never parented together.
    private static readonly Dictionary<long, Transform> FrontCarriers = new Dictionary<long, Transform>();

    private static readonly HashSet<string> LinkedIrons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<IntPtr, bool> LocalCache = new Dictionary<IntPtr, bool>();

    private static readonly Dictionary<IntPtr, HeldAlign> HeldAligns = new Dictionary<IntPtr, HeldAlign>();

    private static readonly Dictionary<string, Vector2> Logged = new Dictionary<string, Vector2>(StringComparer.Ordinal);

    private static bool Bound;

    internal static void Bind(string[] sightNames)
    {
        if (sightNames == null || Bound)
        {
            return;
        }

        Bound = true;
        HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < sightNames.Length; i++)
        {
            string sightName = sightNames[i];
            if (IsDefault(sightName) || Stems.ContainsKey(sightName) || ScopeCatalog.Sights == null || !ScopeCatalog.Sights.TryGetValue(sightName, out uint sightId))
            {
                continue;
            }

            string stem = Stem(sightName, sightId, used);
            Stems.Add(sightName, stem);
            SightsByStem[stem] = sightName;
            NamesById[sightId] = sightName;
        }

        LoadSaved();
    }

    internal static bool Nudge(InventorySlot slot, string sightName, int kind, int direction)
    {
        if (!TryTarget(slot, sightName, out (InventorySlot Slot, string Weapon, string Sight) key, out _))
        {
            return false;
        }

        ReadCurrent(key, out float scope, out float forward);
        float current = kind == 0 ? scope : forward;
        float value = Mathf.Clamp(Mathf.Round((current + (direction * Step)) * 1000f) / 1000f, -Max, Max);
        if (Mathf.Abs(value - current) < 0.0000001f)
        {
            return false;
        }

        if (kind == 0)
        {
            scope = value;
        }
        else
        {
            forward = value;
        }

        Session[key] = new Vector2(scope, forward);
        Reapply();
        return true;
    }

    internal static bool Save(InventorySlot slot, string sightName)
    {
        if (!TryTarget(slot, sightName, out (InventorySlot Slot, string Weapon, string Sight) key, out string weaponTitle))
        {
            return false;
        }

        ReadCurrent(key, out float scope, out float forward);
        Write(key, scope, forward);
        Session.Remove(key);
        Plugin.Instance.Config.Save();
        Plugin.LogSource.LogInfo("Saved alignment for " + SlotName(slot) + " " + weaponTitle + " / " + Label(sightName) + ": scope vertical " + Signed(scope) + ", scope forward " + Signed(forward) + ".");
        Reapply();
        return true;
    }

    internal static bool Reset(InventorySlot slot, string sightName)
    {
        if (!TryTarget(slot, sightName, out (InventorySlot Slot, string Weapon, string Sight) key, out string weaponTitle))
        {
            return false;
        }

        BuiltIn(key.Weapon, key.Sight, out float scopeDefault, out float forwardDefault);
        bool pending = Session.Remove(key);
        ReadSaved(key, out float scope, out float forward);
        if (!pending && Mathf.Abs(scope - scopeDefault) < 0.00001f && Mathf.Abs(forward - forwardDefault) < 0.00001f)
        {
            return false;
        }

        Write(key, scopeDefault, forwardDefault);
        Plugin.Instance.Config.Save();
        Plugin.LogSource.LogInfo("Reset alignment for " + SlotName(slot) + " " + weaponTitle + " / " + Label(sightName) + ".");
        Reapply();
        return true;
    }

    internal static string RowLabel(int kind, int direction, InventorySlot slot, string sightName)
    {
        string action = kind == 0
            ? (direction > 0 ? "Scope up" : "Scope down")
            : (direction > 0 ? "Scope forward" : "Scope backward");
        if (!TryValues(slot, sightName, out float scope, out float forward, out _, out _))
        {
            return action;
        }

        return action + "    " + Signed(kind == 0 ? scope : forward);
    }

    internal static string SaveLabel(InventorySlot slot, string sightName)
    {
        if (TryValues(slot, sightName, out _, out _, out _, out bool pending) && pending)
        {
            return "Save alignment    UNSAVED";
        }

        return "Save alignment";
    }

    internal static string ResetLabel()
    {
        return "Reset alignment";
    }

    internal static string Summary(InventorySlot slot, string sightName, bool selected)
    {
        string slotName = SlotName(slot);
        if (IsDefault(sightName))
        {
            return "Pick a sight to align it on the equipped " + slotName.ToLowerInvariant() + " weapon. Each weapon keeps its own alignment.";
        }

        if (!TryValues(slot, sightName, out float scope, out float forward, out string weaponTitle, out bool pending))
        {
            return "Equip a " + slotName.ToLowerInvariant() + " weapon, then use the rows at the top. Each click moves the sight " + Step.ToString("0.000", CultureInfo.InvariantCulture) + " m.";
        }

        StringBuilder builder = new StringBuilder();
        builder.Append("Scope height ").Append(Signed(scope)).Append(" m");
        builder.Append('\n').Append("Scope forward ").Append(Signed(forward)).Append(" m");
        builder.Append('\n').Append(slotName).Append(" weapon: ").Append(weaponTitle);
        builder.Append('\n').Append(Status(slot, sightName, pending));
        builder.Append('\n').Append(selected
            ? "Top rows nudge this sight. Save stores it for this weapon. Reset restores the default alignment."
            : "Top rows change the selected sight.");
        return builder.ToString();
    }

    internal static void ApplyScope(GearPartHolder holder)
    {
        if (!Plugin.Ready || holder == null || holder.SightPart == null || IsIconCapture(holder) || !IsLocalHolder(holder))
        {
            return;
        }

        Transform sight = holder.SightPart.transform;
        try
        {
            if (!TryPlacement(holder, out float scope, out float forward, out InventorySlot slot, out string weaponKey, out string sightName))
            {
                Transform reset = TrackedIronTarget(sight);
                if (IsTracked(reset))
                {
                    ApplyLocal(reset, 0f, 0f);
                }

                return;
            }

            Transform target = IronTarget(holder, sight, sightName);
            float vertical = VisualSign * scope;
            float depth = ForwardSign * forward;
            if (Mathf.Abs(vertical) < 0.0000001f && Mathf.Abs(depth) < 0.0000001f && !IsTracked(target))
            {
                return;
            }

            ApplyLocal(target, vertical, depth);
            LogIfChanged(slot, weaponKey, sightName, scope, forward);
        }
        finally
        {
            RememberAlign(holder, sight);
        }
    }

    internal static void Forget(GearPartHolder holder)
    {
        if (holder == null)
        {
            return;
        }

        LocalCache.Remove(holder.Pointer);
        long sightId = 0;
        bool hasSight = false;
        long carrierId = 0;
        bool hasCarrier = false;
        if (HeldAligns.TryGetValue(holder.Pointer, out HeldAlign held))
        {
            HeldAligns.Remove(holder.Pointer);
            sightId = held.SightId;
            hasSight = true;
            carrierId = held.CarrierId;
            hasCarrier = held.HasCarrier;
        }

        if (holder.SightPart != null)
        {
            Transform sight = holder.SightPart.transform;
            if (sight != null)
            {
                sightId = sight.GetInstanceID();
                hasSight = true;
            }
        }

        if (!hasSight)
        {
            return;
        }

        Places.Remove(sightId);
        Transform carrier = null;
        if (FrontCarriers.TryGetValue(sightId, out carrier))
        {
            FrontCarriers.Remove(sightId);
        }

        if (carrier != null)
        {
            Places.Remove(carrier.GetInstanceID());
            if (carrier.parent == null)
            {
                UnityEngine.Object.Destroy(carrier.gameObject);
            }
        }
        else if (hasCarrier)
        {
            Places.Remove(carrierId);
        }
    }

    private static void RememberAlign(GearPartHolder holder, Transform sight)
    {
        if (holder == null || sight == null)
        {
            return;
        }

        long sightId = sight.GetInstanceID();
        HeldAlign held = new HeldAlign
        {
            SightId = sightId
        };
        if (FrontCarriers.TryGetValue(sightId, out Transform carrier) && carrier != null)
        {
            held.CarrierId = carrier.GetInstanceID();
            held.HasCarrier = true;
        }

        HeldAligns[holder.Pointer] = held;
    }

    private static Transform TrackedIronTarget(Transform sight)
    {
        if (sight != null && FrontCarriers.TryGetValue(sight.GetInstanceID(), out Transform carrier) && carrier != null)
        {
            return carrier;
        }

        return sight;
    }

    private static Transform IronTarget(GearPartHolder holder, Transform sight, string sightName)
    {
        if (!TryIronMate(sightName, out string mateName, out bool front))
        {
            return sight;
        }

        if (!front)
        {
            AttachMate(sight, sight, mateName);
            return sight;
        }

        return FrontCarrier(holder, sight, sightName, mateName);
    }

    private static Transform FrontCarrier(GearPartHolder holder, Transform sight, string sightName, string mateName)
    {
        long id = sight.GetInstanceID();
        if (!FrontCarriers.TryGetValue(id, out Transform carrier) || carrier == null)
        {
            carrier = sight.parent != null && sight.parent.name == CarrierName ? sight.parent : null;
        }

        if (carrier == null)
        {
            if (Places.TryGetValue(id, out PlaceState state))
            {
                Vector3 local = sight.localPosition;
                local.y -= state.Vertical;
                local.z -= state.Depth;
                sight.localPosition = local;
                Places.Remove(id);
            }

            GameObject carrierObject = new GameObject(CarrierName);
            carrier = carrierObject.transform;
            Transform parent = sight.parent;
            int sibling = sight.GetSiblingIndex();
            carrier.SetParent(parent, false);
            carrier.localPosition = sight.localPosition;
            carrier.localRotation = sight.localRotation;
            carrier.localScale = sight.localScale;
            if (parent != null)
            {
                carrier.SetSiblingIndex(sibling);
            }

            sight.SetParent(carrier, true);
            if (LinkedIrons.Add(sightName))
            {
                Plugin.LogSource.LogInfo("Front iron " + sightName + " is moved with " + mateName + ". The front mesh was not parented to the rear sight.");
            }
        }
        else if (sight.parent != carrier)
        {
            sight.SetParent(carrier, true);
        }

        FrontCarriers[id] = carrier;
        AttachMate(carrier, sight, mateName);
        EnsureSightAlign(holder, carrier, sight);
        return carrier;
    }

    private static void AttachMate(Transform keepOn, Transform sight, string mateName)
    {
        if (keepOn == null || sight == null || string.IsNullOrEmpty(mateName))
        {
            return;
        }

        Transform mate = FindMate(sight, mateName);
        if (mate == null || mate == keepOn || mate == sight || sight.IsChildOf(mate) || mate.parent == keepOn)
        {
            return;
        }

        mate.SetParent(keepOn, true);
    }

    private static Transform FindMate(Transform sight, string mateName)
    {
        Transform parent = sight.parent;
        Transform found = FindDirect(parent, sight, mateName);
        if (found != null)
        {
            return found;
        }

        if (parent != null && parent.name == CarrierName)
        {
            return FindDirect(parent.parent, parent, mateName);
        }

        return null;
    }

    private static Transform FindDirect(Transform parent, Transform skip, string partName)
    {
        if (parent == null)
        {
            return null;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != skip && NamesMatch(child.name, partName))
            {
                return child;
            }
        }

        return null;
    }

    private static void EnsureSightAlign(GearPartHolder holder, Transform carrier, Transform sight)
    {
        Transform align = FindNamed(carrier, "Sight_Align");
        if (align == null)
        {
            GameObject alignObject = new GameObject("Sight_Align");
            align = alignObject.transform;
            align.SetParent(carrier, false);
            Renderer renderer = sight.GetComponent<Renderer>();
            if (renderer != null && renderer.enabled)
            {
                align.position = renderer.bounds.center;
            }
        }

        if (holder.m_aSightLook == null || !holder.m_aSightLook.IsChildOf(carrier))
        {
            holder.m_aSightLook = align;
        }

        ItemEquippable item = holder.GetComponentInParent<ItemEquippable>();
        if (item != null && (item.SightLookAlign == null || !item.SightLookAlign.IsChildOf(carrier)))
        {
            item.SightLookAlign = align;
        }
    }

    private static Transform FindNamed(Transform root, string partName)
    {
        if (root == null)
        {
            return null;
        }

        if (NamesMatch(root.name, partName))
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), partName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool NamesMatch(string transformName, string partName)
    {
        if (string.Equals(transformName, partName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrEmpty(transformName) || string.IsNullOrEmpty(partName) || transformName.Length <= partName.Length)
        {
            return false;
        }

        if (!transformName.StartsWith(partName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        char next = transformName[partName.Length];
        return next == '(' || next == ' ';
    }

    private static bool TryIronMate(string sightName, out string mateName, out bool front)
    {
        mateName = null;
        front = false;
        const string prefix = "IronSights_";
        if (string.IsNullOrEmpty(sightName) || sightName.Length <= prefix.Length || !sightName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string body = sightName.Substring(prefix.Length);
        if (body.EndsWith("_Copy_Copy", StringComparison.OrdinalIgnoreCase))
        {
            body = body.Substring(0, body.Length - "_Copy_Copy".Length);
        }
        else if (body.EndsWith("_Copy", StringComparison.OrdinalIgnoreCase))
        {
            body = body.Substring(0, body.Length - "_Copy".Length);
        }

        bool rear = body.EndsWith("_B", StringComparison.OrdinalIgnoreCase);
        front = body.EndsWith("_F", StringComparison.OrdinalIgnoreCase);
        if (!front && !rear)
        {
            return false;
        }

        string id = body.Substring(0, body.Length - 2);
        if (id.Length == 0)
        {
            return false;
        }

        mateName = sightName.Substring(0, prefix.Length) + id + (front ? "_B" : "_F");
        return true;
    }

    private static void Reapply()
    {
        try
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<GearPartHolder> holders = UnityEngine.Object.FindObjectsOfType<GearPartHolder>();
            if (holders != null)
            {
                for (int i = 0; i < holders.Count; i++)
                {
                    ApplyScope(holders[i]);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Alignment refresh: " + ex.Message);
        }
    }

    private static void ApplyLocal(Transform target, float vertical, float depth)
    {
        if (target == null)
        {
            return;
        }

        Vector3 position = OffsetAxes(target.GetInstanceID(), target.localPosition, vertical, depth);
        if ((position - target.localPosition).sqrMagnitude > 0.00000001f)
        {
            target.localPosition = position;
        }
    }

    private static Vector3 OffsetAxes(long id, Vector3 position, float vertical, float depth)
    {
        if (Places.TryGetValue(id, out PlaceState state) && (position - state.Written).sqrMagnitude <= 0.00000001f)
        {
            if (Mathf.Abs(state.Vertical - vertical) <= 0.0000001f && Mathf.Abs(state.Depth - depth) <= 0.0000001f)
            {
                return position;
            }

            position.y += vertical - state.Vertical;
            position.z += depth - state.Depth;
        }
        else
        {
            position.y += vertical;
            position.z += depth;
        }

        Places[id] = new PlaceState
        {
            Written = position,
            Vertical = vertical,
            Depth = depth
        };
        return position;
    }

    private static bool IsTracked(Transform target)
    {
        return target != null && Places.ContainsKey(target.GetInstanceID());
    }

    private static void ForgetTransform(Transform target)
    {
        if (target != null)
        {
            Places.Remove(target.GetInstanceID());
        }
    }

    private static bool IsIconCapture(GearPartHolder holder)
    {
        GearIconRendering rendering = GearIconRendering.Current;
        GearIconRendering.IconRenderJob job = rendering != null ? rendering.m_currentJob : null;
        GameObject loaded = job != null ? job.loadedGearGO : null;
        if (loaded == null)
        {
            return false;
        }

        Transform root = loaded.transform;
        Transform current = holder.transform;
        return current == root || current.IsChildOf(root);
    }

    private static bool IsLocalHolder(GearPartHolder holder)
    {
        if (LocalCache.TryGetValue(holder.Pointer, out bool local))
        {
            return local;
        }

        local = true;
        Transform current = holder.transform;
        while (current != null)
        {
            CM_PlayerLobbyBar bar = current.GetComponent<CM_PlayerLobbyBar>();
            if (bar != null && bar.m_player != null)
            {
                local = bar.m_player.IsLocal;
                break;
            }

            PlayerAgent agent = current.GetComponent<PlayerAgent>();
            if (agent != null)
            {
                local = agent.IsLocallyOwned;
                break;
            }

            current = current.parent;
        }

        LocalCache[holder.Pointer] = local;
        return local;
    }

    private static bool TryPlacement(GearPartHolder holder, out float scope, out float forward, out InventorySlot slot, out string weaponKey, out string sightName)
    {
        scope = 0f;
        forward = 0f;
        slot = default;
        weaponKey = null;
        sightName = null;
        if (!TryContext(holder, out slot, out weaponKey, out sightName))
        {
            return false;
        }

        ReadCurrent((slot, weaponKey, sightName), out scope, out forward);
        return true;
    }

    private static bool TryContext(GearPartHolder holder, out InventorySlot slot, out string weaponKey, out string sightName)
    {
        slot = default;
        weaponKey = null;
        sightName = NameOf(holder != null ? holder.SightData : null);
        if (IsDefault(sightName))
        {
            return false;
        }

        GearIDRange range = holder.GearIDRange;
        if (range == null)
        {
            ItemEquippable item = holder.GetComponentInParent<ItemEquippable>();
            range = item != null ? item.GearIDRange : null;
        }

        if (range != null)
        {
            if (!ScopeCatalog.AppliesTo(range))
            {
                return false;
            }

            weaponKey = ScopeCatalog.WeaponKeyFor(range);
            if (weaponKey != null && TrySlot(weaponKey, sightName, out slot))
            {
                return true;
            }
        }

        if (!ScopeLoadoutCategory.TryEditing(out slot) || !TryEquipped(slot, out weaponKey, out _, out _))
        {
            weaponKey = null;
            return false;
        }

        return true;
    }

    private static bool TrySlot(string weaponKey, string sightName, out InventorySlot slot)
    {
        bool primary = string.Equals(EquippedKey(InventorySlot.GearStandard), weaponKey, StringComparison.Ordinal);
        bool special = string.Equals(EquippedKey(InventorySlot.GearSpecial), weaponKey, StringComparison.Ordinal);
        if (primary && !special)
        {
            slot = InventorySlot.GearStandard;
            return true;
        }

        if (special && !primary)
        {
            slot = InventorySlot.GearSpecial;
            return true;
        }

        if (primary && special)
        {
            bool specialSight = string.Equals(ScopeChoices.Display(InventorySlot.GearSpecial), sightName, StringComparison.Ordinal);
            bool primarySight = string.Equals(ScopeChoices.Display(InventorySlot.GearStandard), sightName, StringComparison.Ordinal);
            slot = specialSight && !primarySight ? InventorySlot.GearSpecial : InventorySlot.GearStandard;
            return true;
        }

        slot = default;
        return false;
    }

    private static string EquippedKey(InventorySlot slot)
    {
        return TryEquipped(slot, out string weaponKey, out _, out _) ? weaponKey : null;
    }

    private static bool TryEquipped(InventorySlot slot, out string weaponKey, out string weaponTitle, out GearIDRange range)
    {
        weaponKey = null;
        weaponTitle = null;
        range = null;
        BackpackItem item = PlayerBackpackManager.GetLocalItem(slot);
        range = item != null ? item.GearIDRange : null;
        if (range == null)
        {
            return false;
        }

        if (!ScopeCatalog.AppliesTo(range))
        {
            return false;
        }

        weaponKey = ScopeCatalog.WeaponKeyFor(range);
        if (weaponKey == null)
        {
            return false;
        }

        weaponTitle = ScopeCatalog.WeaponTitleFor(weaponKey, range);
        return true;
    }

    private static bool TryTarget(InventorySlot slot, string sightName, out (InventorySlot Slot, string Weapon, string Sight) key, out string weaponTitle)
    {
        key = default;
        weaponTitle = null;
        if (!TryEquipped(slot, out string weaponKey, out weaponTitle, out _) || !Stems.ContainsKey(sightName))
        {
            return false;
        }

        key = (slot, weaponKey, sightName);
        return true;
    }

    private static bool TryValues(InventorySlot slot, string sightName, out float scope, out float forward, out string weaponTitle, out bool pending)
    {
        scope = 0f;
        forward = 0f;
        weaponTitle = null;
        pending = false;
        if (!TryTarget(slot, sightName, out (InventorySlot Slot, string Weapon, string Sight) key, out weaponTitle))
        {
            return false;
        }

        ReadCurrent(key, out scope, out forward);
        ReadSaved(key, out float savedScope, out float savedForward);
        pending = Session.ContainsKey(key)
            && (Mathf.Abs(savedScope - scope) > 0.00001f || Mathf.Abs(savedForward - forward) > 0.00001f);
        return true;
    }

    private static void ReadCurrent((InventorySlot Slot, string Weapon, string Sight) key, out float scope, out float forward)
    {
        if (Session.TryGetValue(key, out Vector2 session))
        {
            scope = Mathf.Clamp(session.x, -Max, Max);
            forward = Mathf.Clamp(session.y, -Max, Max);
            return;
        }

        ReadSaved(key, out scope, out forward);
    }

    private static void ReadSaved((InventorySlot Slot, string Weapon, string Sight) key, out float scope, out float forward)
    {
        if (!Saved.TryGetValue(key, out Pair pair))
        {
            BuiltIn(key.Weapon, key.Sight, out scope, out forward);
            return;
        }

        scope = Mathf.Clamp(pair.Scope.Value, -Max, Max);
        forward = Mathf.Clamp(pair.Forward.Value, -Max, Max);
    }

    private static void BuiltIn(string weaponKey, string sightName, out float scope, out float forward)
    {
        if (weaponKey == null || !ScopeCatalog.TryAlignmentDefault(weaponKey, out scope, out forward))
        {
            scope = 0f;
            forward = 0f;
        }

        if (sightName != null && SightForwardShift.TryGetValue(sightName, out float shift))
        {
            forward += shift;
        }

        scope = Snap(scope);
        forward = Snap(forward);
    }

    private static float Snap(float value)
    {
        return Mathf.Clamp(Mathf.Round(value * 1000f) / 1000f, -Max, Max);
    }

    private static string Status(InventorySlot slot, string sightName, bool pending)
    {
        if (pending)
        {
            return "Not saved yet.";
        }

        if (TryTarget(slot, sightName, out (InventorySlot Slot, string Weapon, string Sight) key, out _) && Saved.ContainsKey(key))
        {
            return "Saved for this weapon and sight.";
        }

        return "Default alignment for this weapon.";
    }

    private static void Write((InventorySlot Slot, string Weapon, string Sight) key, float scope, float forward)
    {
        if (!Ensure(key))
        {
            return;
        }

        Pair pair = Saved[key];
        pair.Scope.Value = Mathf.Clamp(scope, -Max, Max);
        pair.Forward.Value = Mathf.Clamp(forward, -Max, Max);
    }

    private static bool Ensure((InventorySlot Slot, string Weapon, string Sight) key)
    {
        if (Saved.ContainsKey(key))
        {
            return true;
        }

        if (!Stems.TryGetValue(key.Sight, out string stem))
        {
            return false;
        }

        string weapon = SlotName(key.Slot) + " weapon " + key.Weapon + ", sight " + key.Sight + ". Meters. Stored for this weapon and sight only.";
        BuiltIn(key.Weapon, key.Sight, out float scopeDefault, out float forwardDefault);
        Saved[key] = new Pair
        {
            Scope = Plugin.Instance.Config.Bind(Section, ConfigKey(key.Slot, key.Weapon, stem, 0), scopeDefault, weapon + " Positive moves up."),
            Forward = Plugin.Instance.Config.Bind(Section, ConfigKey(key.Slot, key.Weapon, stem, 1), forwardDefault, weapon + " Positive moves the sight forward, toward the muzzle.")
        };
        return true;
    }

    private static void LoadSaved()
    {
        string path = Path.Combine(Paths.ConfigPath, "SelectableScopes.cfg");
        if (!File.Exists(path))
        {
            return;
        }

        bool inSection = false;
        bool retiredNoted = false;
        HashSet<(InventorySlot Slot, string Weapon, string Sight)> pairs = new HashSet<(InventorySlot, string, string)>();
        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                inSection = string.Equals(trimmed.Substring(1, trimmed.Length - 2), Section, StringComparison.Ordinal);
                continue;
            }

            if (!inSection || trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string configKey = trimmed.Substring(0, separator).Trim();
            if (configKey.EndsWith(RetiredHandsSuffix, StringComparison.Ordinal))
            {
                if (!retiredNoted)
                {
                    retiredNoted = true;
                    Plugin.LogSource.LogInfo("Aim hands alignment entries are ignored. Forward and backward alignment replaces them.");
                }

                continue;
            }

            if (TryParseConfigKey(configKey, out InventorySlot slot, out string weaponKey, out string stem, out _))
            {
                if (SightsByStem.TryGetValue(stem, out string sightName))
                {
                    pairs.Add((slot, weaponKey, sightName));
                }
                else
                {
                    Plugin.LogSource.LogWarning("Unknown alignment entry '" + configKey + "'. It was not loaded.");
                }

                continue;
            }

            if (!configKey.EndsWith(ScopeSuffix, StringComparison.Ordinal) && !configKey.EndsWith(ForwardSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            string raw = trimmed.Substring(separator + 1).Trim();
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float legacy) && Mathf.Abs(legacy) > 0.00001f)
            {
                Plugin.LogSource.LogInfo("Ignored shared alignment '" + configKey + "' = " + Signed(legacy) + ". Save it again for each weapon.");
            }
        }

        foreach ((InventorySlot Slot, string Weapon, string Sight) pair in pairs)
        {
            Ensure(pair);
        }
    }

    private static bool TryParseConfigKey(string key, out InventorySlot slot, out string weaponKey, out string stem, out int kind)
    {
        slot = default;
        weaponKey = null;
        stem = null;
        kind = 0;
        string suffix = null;
        if (key.EndsWith(ScopeSuffix, StringComparison.Ordinal))
        {
            suffix = ScopeSuffix;
        }
        else if (key.EndsWith(ForwardSuffix, StringComparison.Ordinal))
        {
            kind = 1;
            suffix = ForwardSuffix;
        }

        if (suffix == null)
        {
            return false;
        }

        string body = key.Substring(0, key.Length - suffix.Length);
        int first = body.IndexOf(' ');
        if (first <= 0)
        {
            return false;
        }

        string slotName = body.Substring(0, first);
        if (string.Equals(slotName, "Primary", StringComparison.Ordinal))
        {
            slot = InventorySlot.GearStandard;
        }
        else if (string.Equals(slotName, "Special", StringComparison.Ordinal))
        {
            slot = InventorySlot.GearSpecial;
        }
        else
        {
            return false;
        }

        string rest = body.Substring(first + 1);
        int second = rest.IndexOf(' ');
        if (second <= 0)
        {
            return false;
        }

        weaponKey = rest.Substring(0, second);
        stem = rest.Substring(second + 1);
        return weaponKey.Length > 0 && stem.Length > 0;
    }

    private static string ConfigKey(InventorySlot slot, string weaponKey, string stem, int kind)
    {
        return SlotName(slot) + " " + weaponKey + " " + stem + (kind == 0 ? ScopeSuffix : ForwardSuffix);
    }

    private static void LogIfChanged(InventorySlot slot, string weaponKey, string sightName, float scope, float forward)
    {
        string id = SlotName(slot) + "|" + weaponKey + "|" + sightName;
        if (Logged.TryGetValue(id, out Vector2 previous)
            && Mathf.Abs(previous.x - scope) < 0.00001f
            && Mathf.Abs(previous.y - forward) < 0.00001f)
        {
            return;
        }

        bool first = !Logged.ContainsKey(id);
        Logged[id] = new Vector2(scope, forward);
        if (first && Mathf.Abs(scope) < 0.00001f && Mathf.Abs(forward) < 0.00001f)
        {
            return;
        }

        Plugin.LogSource.LogInfo("Alignment " + SlotName(slot) + " " + weaponKey + " / " + Label(sightName) + ": scope vertical " + Signed(scope) + ", scope forward " + Signed(forward) + ".");
    }

    private static string NameOf(GearSightPartDataBlock block)
    {
        if (block == null)
        {
            return null;
        }

        if (NamesById.TryGetValue(block.persistentID, out string sightName))
        {
            return sightName;
        }

        return string.IsNullOrWhiteSpace(block.name) ? null : block.name;
    }

    private static string Stem(string sightName, uint sightId, HashSet<string> used)
    {
        StringBuilder builder = new StringBuilder(sightName.Length);
        foreach (char character in sightName)
        {
            bool reserved = character switch
            {
                '\t' or '\n' or '\r' or '[' or ']' or '=' or '#' or ' ' => true,
                _ => false
            };
            builder.Append(reserved ? '_' : character);
        }

        string stem = builder.ToString().Trim('_');
        if (stem.Length == 0)
        {
            stem = "Sight";
        }

        if (!used.Add(stem))
        {
            stem = stem + "_" + sightId.ToString(CultureInfo.InvariantCulture);
            used.Add(stem);
        }

        return stem;
    }

    private static string Label(string sightName)
    {
        string title = ScopeCatalog.Title(sightName);
        if (string.IsNullOrWhiteSpace(title) || string.Equals(title, sightName, StringComparison.Ordinal))
        {
            return sightName;
        }

        return sightName + " (" + title + ")";
    }

    private static string SlotName(InventorySlot slot)
    {
        return slot == InventorySlot.GearSpecial ? "Special" : "Primary";
    }

    private static string Signed(float value)
    {
        return value.ToString("+0.0000;-0.0000", CultureInfo.InvariantCulture);
    }

    private static bool IsDefault(string sightName)
    {
        return string.IsNullOrEmpty(sightName) || string.Equals(sightName, GearJsonEditor.DefaultSight, StringComparison.Ordinal);
    }

    private struct Pair
    {
        public ConfigEntry<float> Scope;

        public ConfigEntry<float> Forward;
    }

    private struct PlaceState
    {
        public Vector3 Written;

        public float Vertical;

        public float Depth;
    }

    private struct HeldAlign
    {
        public long SightId;

        public long CarrierId;

        public bool HasCarrier;
    }
}
