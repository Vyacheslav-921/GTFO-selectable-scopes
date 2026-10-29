using System.Globalization;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Localization;
using Player;

namespace SelectableScopes;

internal static class ScopeCatalog
{
    internal static Dictionary<string, uint> Sights { get; private set; }

    internal static string[] SightNames { get; private set; }

    internal static Dictionary<uint, ConfigEntry<string>> Weapons { get; private set; }

    internal static void BuildAndApply()
    {
        if (!Plugin.Ready)
        {
            if (!TryBuild())
            {
                return;
            }

            Plugin.Ready = true;
        }

        ScopeApplier.ApplyConfigured();
    }

    private static bool TryBuild()
    {
        Il2CppArrayBase<GearSightPartDataBlock> sights = GameDataBlockBase<GearSightPartDataBlock>.GetAllBlocks();
        Il2CppArrayBase<PlayerOfflineGearDataBlock> gear = GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks();
        if (sights == null || sights.Count == 0 || gear == null)
        {
            Plugin.LogSource.LogWarning("Sight datablocks are not loaded yet.");
            return false;
        }

        Sights = new Dictionary<string, uint>(StringComparer.Ordinal);
        List<string> names = new List<string> { GearJsonEditor.DefaultSight };
        foreach (GearSightPartDataBlock block in sights)
        {
            if (block == null)
            {
                continue;
            }

            string name = block.name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "ID_" + block.persistentID;
            }

            if (string.Equals(name, GearJsonEditor.DefaultSight, StringComparison.Ordinal) || Sights.ContainsKey(name))
            {
                name = name + "#" + block.persistentID;
            }

            if (!Sights.ContainsKey(name))
            {
                Sights.Add(name, block.persistentID);
                names.Add(name);
            }
        }

        names.Sort(1, names.Count - 1, StringComparer.OrdinalIgnoreCase);
        SightNames = names.ToArray();
        AcceptableValueList<string> acceptable = new AcceptableValueList<string>(SightNames);
        ScopeChoices.Bind(acceptable);
        Weapons = new Dictionary<uint, ConfigEntry<string>>();
        HashSet<string> usedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "General", "Lobby" };
        Dictionary<string, string> existing = ReadExistingSights();
        int configured = 0;
        foreach (PlayerOfflineGearDataBlock block in gear)
        {
            if (block == null || string.IsNullOrEmpty(block.GearJSON) || !ShouldConfigure(block.GearJSON) || Weapons.ContainsKey(block.persistentID))
            {
                continue;
            }

            string section = SectionName(block, usedSections);
            if (existing.TryGetValue(section, out string saved)
                && !string.Equals(saved, GearJsonEditor.DefaultSight, StringComparison.Ordinal)
                && !Sights.ContainsKey(saved))
            {
                Plugin.LogSource.LogWarning("Unknown sight '" + saved + "' for " + section + ". Leaving that weapon unchanged.");
            }

            string display = GearJsonEditor.GetDisplayName(block.GearJSON) ?? block.name;
            ConfigEntry<string> entry = Plugin.Instance.Config.Bind(
                section,
                "Sight",
                GearJsonEditor.DefaultSight,
                new ConfigDescription(
                    "In-game name: " + display + ". Persistent ID: " + block.persistentID + ". Default keeps the current sight. Any other value is a GearSightPart datablock name.",
                    acceptable));
            Weapons.Add(block.persistentID, entry);
            configured++;
        }

        Plugin.Instance.Config.Save();
        Plugin.LogSource.LogInfo("Selectable Scopes catalog: " + Sights.Count + " sights, " + configured + " weapons.");
        return true;
    }

    private static bool ShouldConfigure(string gearJson)
    {
        if (GearJsonEditor.TryGetComponentValue(gearJson, GearJsonEditor.SightComponentId, out _))
        {
            return true;
        }

        return IsFirearm(gearJson);
    }

    private static bool IsFirearm(string gearJson)
    {
        if (!GearJsonEditor.TryGetComponentValue(gearJson, GearJsonEditor.CategoryComponentId, out long value))
        {
            return false;
        }

        GearCategoryDataBlock category;
        try
        {
            category = GameDataBlockBase<GearCategoryDataBlock>.GetBlock((uint)value);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Category " + value + " was not readable: " + ex.Message);
            return false;
        }

        if (category == null)
        {
            return false;
        }

        ItemDataBlock item;
        try
        {
            item = GameDataBlockBase<ItemDataBlock>.GetBlock(category.BaseItem);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Item " + category.BaseItem + " was not readable: " + ex.Message);
            return false;
        }

        if (item == null)
        {
            return false;
        }

        InventorySlot slot = item.inventorySlot;
        return slot == InventorySlot.GearStandard || slot == InventorySlot.GearSpecial;
    }

    private static Dictionary<string, string> ReadExistingSights()
    {
        Dictionary<string, string> sights = new Dictionary<string, string>(StringComparer.Ordinal);
        string path = Path.Combine(Paths.ConfigPath, "SelectableScopes.cfg");
        if (!File.Exists(path))
        {
            return sights;
        }

        string section = null;
        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                section = trimmed.Substring(1, trimmed.Length - 2);
            }
            else if (section != null && trimmed.Length != 0 && trimmed[0] != '#')
            {
                int separator = trimmed.IndexOf('=');
                if (separator > 0 && string.Equals(trimmed.Substring(0, separator).Trim(), "Sight", StringComparison.Ordinal))
                {
                    sights[section] = trimmed.Substring(separator + 1).Trim();
                }
            }
        }

        return sights;
    }

    internal static string Title(string sightName)
    {
        if (IsDefault(sightName))
        {
            return "Default";
        }

        GearSightPartDataBlock block = Find(sightName);
        string publicName = block?.General?.PublicName;
        if (!string.IsNullOrWhiteSpace(publicName))
        {
            string localized = Localize(publicName.Trim());
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized.IndexOf('_') >= 0 && localized.IndexOf(' ') < 0 ? Prettify(localized) : localized;
            }
        }

        return Prettify(block != null && !string.IsNullOrWhiteSpace(block.name) ? block.name : sightName);
    }

    internal static string Describe(string sightName, InventorySlot slot, bool selected)
    {
        string weapon = slot == InventorySlot.GearSpecial ? "special" : "primary";
        StringBuilder builder = new StringBuilder();
        if (selected)
        {
            builder.Append("Selected for your ").Append(weapon).Append('.').Append('\n').Append('\n');
        }

        if (IsDefault(sightName))
        {
            builder.Append("Keeps the sight already attached to the weapon.");
        }
        else
        {
            AppendStats(builder, sightName);
            builder.Append("Replaces the sight on your equipped ").Append(weapon).Append('.');
        }

        builder.Append('\n').Append("Takes effect the next time that weapon is equipped.");
        return builder.ToString();
    }

    private static void AppendStats(StringBuilder builder, string sightName)
    {
        GearSightPartDataBlock block = Find(sightName);
        GearSightPartProperties properties = block?.SightProperties;
        if (properties != null)
        {
            if (properties.GearFovZoom > 0)
            {
                builder.Append("Aim field of view: ").Append(properties.GearFovZoom).Append('\n');
            }

            if (properties.WorldFovZoom > 0)
            {
                builder.Append("World field of view: ").Append(properties.WorldFovZoom).Append('\n');
            }

            if (properties.ZoomSensitivityModifier > 0f)
            {
                int percent = (int)Math.Round(properties.ZoomSensitivityModifier * 100f);
                builder.Append("Aim sensitivity: ").Append(percent.ToString(CultureInfo.InvariantCulture)).Append("%\n");
            }
        }

        string dataName = block != null && !string.IsNullOrWhiteSpace(block.name) ? block.name : sightName;
        string title = Title(sightName);
        if (!string.Equals(title, dataName, StringComparison.Ordinal) && !string.Equals(title, Prettify(dataName), StringComparison.Ordinal))
        {
            builder.Append("Data name: ").Append(dataName).Append('\n');
        }
    }

    private static bool IsDefault(string sightName)
    {
        return string.IsNullOrEmpty(sightName) || string.Equals(sightName, GearJsonEditor.DefaultSight, StringComparison.Ordinal);
    }

    private static GearSightPartDataBlock Find(string sightName)
    {
        if (Sights == null || string.IsNullOrEmpty(sightName) || !Sights.TryGetValue(sightName, out uint sightId))
        {
            return null;
        }

        try
        {
            return GameDataBlockBase<GearSightPartDataBlock>.GetBlock(sightId);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Sight " + sightName + " was not readable: " + ex.Message);
            return null;
        }
    }

    private static string Localize(string value)
    {
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
        {
            return value;
        }

        try
        {
            string text = Text.Get(id);
            return string.IsNullOrWhiteSpace(text) ? value : text;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Sight name " + id + " was not localized: " + ex.Message);
            return value;
        }
    }

    private static string Prettify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Sight";
        }

        string text = name.Replace('_', ' ').Replace('#', ' ').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (text.StartsWith("Sight ", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring("Sight ".Length);
        }

        return text.Length == 0 ? name : text;
    }

    private static string SectionName(PlayerOfflineGearDataBlock gear, HashSet<string> usedSections)
    {
        string name = gear.name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Gear" + gear.persistentID;
        }

        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            bool reserved = character switch
            {
                '\t' or '\n' or '\r' or '[' or ']' => true,
                _ => false
            };
            builder.Append(reserved ? '_' : character);
        }

        string section = builder.ToString().Trim();
        if (section.Length == 0)
        {
            section = "Gear" + gear.persistentID;
        }

        if (!usedSections.Add(section))
        {
            section = section + "_" + gear.persistentID;
            usedSections.Add(section);
        }

        return section;
    }
}
