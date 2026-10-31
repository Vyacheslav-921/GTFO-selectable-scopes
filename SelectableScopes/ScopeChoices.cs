using BepInEx.Configuration;
using Player;

namespace SelectableScopes;

internal static class ScopeChoices
{
    internal static ConfigEntry<string> Primary { get; private set; }

    internal static ConfigEntry<string> Special { get; private set; }

    internal static void Bind(AcceptableValueList<string> acceptable)
    {
        Primary = BindEntry(
            "Primary Sight",
            "Sight used on your equipped primary. Default keeps the weapon's current sight.",
            acceptable);
        Special = BindEntry(
            "Special Sight",
            "Sight used on your equipped special. Default keeps the weapon's current sight.",
            acceptable);
    }

    internal static string Display(InventorySlot slot)
    {
        ConfigEntry<string> entry = Entry(slot);
        if (entry == null || string.IsNullOrEmpty(entry.Value))
        {
            return GearJsonEditor.DefaultSight;
        }

        return entry.Value;
    }

    internal static void Set(InventorySlot slot, string sightName)
    {
        ConfigEntry<string> entry = Entry(slot);
        if (entry == null)
        {
            return;
        }

        entry.Value = sightName;
        Plugin.Instance.Config.Save();
    }

    internal static bool TryGetSightId(InventorySlot slot, out uint sightId)
    {
        sightId = 0;
        ConfigEntry<string> entry = Entry(slot);
        if (entry == null || ScopeCatalog.Sights == null)
        {
            return false;
        }

        string choice = entry.Value;
        if (string.IsNullOrEmpty(choice) || string.Equals(choice, GearJsonEditor.DefaultSight, StringComparison.Ordinal))
        {
            return false;
        }

        return ScopeCatalog.Sights.TryGetValue(choice, out sightId);
    }

    private static ConfigEntry<string> BindEntry(string key, string description, AcceptableValueList<string> acceptable)
    {
        ConfigEntry<string> entry = Plugin.Instance.Config.Bind(
            "Lobby",
            key,
            GearJsonEditor.DefaultSight,
            new ConfigDescription(description, acceptable));
        if (ScopeCatalog.IsUnusableScope(entry.Value))
        {
            Plugin.LogSource.LogInfo("Lobby " + key + " was " + entry.Value + ". That scope is no longer in the menu, so the slot is back on Default.");
            entry.Value = GearJsonEditor.DefaultSight;
        }

        if (!string.IsNullOrEmpty(entry.Value)
            && !string.Equals(entry.Value, GearJsonEditor.DefaultSight, StringComparison.Ordinal)
            && (ScopeCatalog.Sights == null || !ScopeCatalog.Sights.ContainsKey(entry.Value)))
        {
            Plugin.LogSource.LogWarning("Unknown sight '" + entry.Value + "' for Lobby " + key + ". That slot will keep the weapon's current sight.");
        }

        return entry;
    }

    private static ConfigEntry<string> Entry(InventorySlot slot)
    {
        if (slot == InventorySlot.GearStandard)
        {
            return Primary;
        }

        if (slot == InventorySlot.GearSpecial)
        {
            return Special;
        }

        return null;
    }
}
