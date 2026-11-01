using System.Globalization;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using GameData;
using Gear;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Localization;
using Player;

namespace SelectableScopes;

internal static class ScopeCatalog
{
    internal static Dictionary<string, uint> Sights { get; private set; }

    internal static string[] SightNames { get; private set; }

    internal static Dictionary<uint, ConfigEntry<string>> Weapons { get; private set; }

    private static readonly Dictionary<string, string> WeaponKeys = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> WeaponTitles = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly Dictionary<string, AuthoredSight> AuthoredSights = new Dictionary<string, AuthoredSight>(StringComparer.Ordinal);

    private static readonly Dictionary<string, AlignmentDefault> AlignmentDefaults = new Dictionary<string, AlignmentDefault>(StringComparer.Ordinal);

    // Every sight on these weapons starts here until that sight is saved. Vertical stays 0 when it is omitted.
    private static readonly Dictionary<string, AlignmentDefault> AlignmentPresets = new Dictionary<string, AlignmentDefault>(StringComparer.OrdinalIgnoreCase)
    {
        { "HighCalPistol", new AlignmentDefault { Vertical = -0.035f, Forward = -0.085f } }, // Shelling Arid 5
        { "PistolSemi", new AlignmentDefault { Vertical = -0.035f, Forward = -0.07f } }, // Shelling S49
        { "PistolBurst", new AlignmentDefault { Vertical = -0.02f, Forward = -0.072f } }, // Shelling Nano
        { "PistolAuto", new AlignmentDefault { Vertical = 0.005f, Forward = 0.048f } }, // Shelling Nano
        { "Autopistol staggering", new AlignmentDefault { Vertical = 0f, Forward = -0.045f } }, // Raptus Steigro, HEL autopistol
        { "ShotgunSemi", new AlignmentDefault { Vertical = 0f, Forward = -0.06f } }, // Buckland s870
        { "BullpupAuto", new AlignmentDefault { Vertical = 0f, Forward = -0.02f } }, // Accrat Golok DA
        { "ShotgunAuto", new AlignmentDefault { Vertical = 0f, Forward = -0.01f } }, // Buckland AF6
        { "SMGAuto", new AlignmentDefault { Vertical = 0.02f, Forward = 0.111f } }, // Van Auken LTC5
        { "PDWAuto", new AlignmentDefault { Vertical = -0.02f, Forward = -0.12f } }, // Accrat STB
        { "ShotgunRanged", new AlignmentDefault { Vertical = 0.02f, Forward = 0.115f } }, // Buckland XDist2
        { "SMGHeavyAuto", new AlignmentDefault { Vertical = 0.02f, Forward = 0.111f } }, // Accrat ND6
        { "SMGBurst", new AlignmentDefault { Vertical = 0f, Forward = -0.07f } }, // Van Auken CAB F4
        { "HELGun", new AlignmentDefault { Vertical = 0f, Forward = -0.1f } }, // Omneco EXP1
        { "DMRSemi", new AlignmentDefault { Vertical = 0f, Forward = -0.06f } }, // TR22 Hanaway
        { "DMRBurst", new AlignmentDefault { Vertical = 0f, Forward = -0.05f } }, // Hanaway PSB
        { "HELRifleSemi", new AlignmentDefault { Vertical = 0f, Forward = -0.1f } }, // Omneco LRG
        { "MachinegunAuto", new AlignmentDefault { Vertical = 0.02f, Forward = 0.17f } }, // Techman Veruta XII
        { "ShotgunShort", new AlignmentDefault { Vertical = 0f, Forward = 0.035f } } // Buckland SBS III
    };

    private static readonly HashSet<string> ExcludedSignatures = new HashSet<string>(StringComparer.Ordinal);

    private static readonly HashSet<string> HiddenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static eGearComponent[] Components;

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
        HiddenNames.Clear();
        List<string> names = new List<string> { GearJsonEditor.DefaultSight };
        int skippedFrontIrons = 0;
        int skippedUnusable = 0;
        foreach (GearSightPartDataBlock block in sights)
        {
            if (block == null)
            {
                continue;
            }

            if (IsFrontIronSight(block.name))
            {
                skippedFrontIrons++;
                continue;
            }

            if (IsHiddenScope(block))
            {
                skippedUnusable++;
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
        ExcludedSignatures.Clear();
        HashSet<string> usedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "General", "Lobby", "Alignment" };
        Dictionary<string, string> existing = ReadExistingSights();
        int configured = 0;
        List<string> revolvers = new List<string>();
        foreach (PlayerOfflineGearDataBlock block in gear)
        {
            if (block == null || string.IsNullOrEmpty(block.GearJSON) || Weapons.ContainsKey(block.persistentID))
            {
                continue;
            }

            if (IsRevolver(block.name))
            {
                RememberRevolver(block);
                revolvers.Add(string.IsNullOrWhiteSpace(block.name) ? block.persistentID.ToString(CultureInfo.InvariantCulture) : block.name);
                continue;
            }

            if (!ShouldConfigure(block.GearJSON))
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
            IndexWeapon(block, display);
            configured++;
        }

        ScopeAlignment.Bind(SightNames);
        DropRevolverSettings(revolvers);
        Plugin.Instance.Config.Save();
        Plugin.LogSource.LogInfo("Selectable Scopes catalog: " + Sights.Count + " sights, " + configured + " weapons.");
        if (revolvers.Count > 0)
        {
            revolvers.Sort(StringComparer.OrdinalIgnoreCase);
            Plugin.LogSource.LogInfo("Left " + string.Join(", ", revolvers) + " on the default sight.");
        }
        if (skippedFrontIrons > 0)
        {
            Plugin.LogSource.LogInfo("Left out " + skippedFrontIrons + " front iron sights. The rear sight is the one that aims.");
        }
        if (skippedUnusable > 0)
        {
            Plugin.LogSource.LogInfo("Left out " + skippedUnusable + " unusable sights (Sight 11, IRON_SIGHTS 2B, IRON_SIGHTS 11).");
        }
        return true;
    }

    internal static string WeaponKeyFor(GearIDRange range)
    {
        if (range == null)
        {
            return null;
        }

        string signature = null;
        try
        {
            signature = Signature(range);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Weapon signature: " + ex.Message);
        }

        if (signature != null && WeaponKeys.TryGetValue(signature, out string weaponKey))
        {
            return weaponKey;
        }

        uint category = range.GetCompID(eGearComponent.Category);
        uint receiver = range.GetCompID(eGearComponent.ReceiverPart);
        if (category == 0 && receiver == 0)
        {
            string name = range.PublicGearName;
            return string.IsNullOrWhiteSpace(name) ? null : "name-" + SanitizeKey(name);
        }

        return "cat-" + category.ToString(CultureInfo.InvariantCulture) + "-rec-" + receiver.ToString(CultureInfo.InvariantCulture);
    }

    internal static string WeaponTitleFor(string weaponKey, GearIDRange range)
    {
        if (weaponKey != null && WeaponTitles.TryGetValue(weaponKey, out string title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (range != null && !string.IsNullOrWhiteSpace(range.PublicGearName))
        {
            return range.PublicGearName;
        }

        return "Weapon";
    }

    internal static bool TryGetAuthoredSight(GearIDRange range, out bool hadSight, out uint sightId)
    {
        hadSight = false;
        sightId = 0;
        if (range == null)
        {
            return false;
        }

        string signature;
        try
        {
            signature = Signature(range);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Weapon signature: " + ex.Message);
            return false;
        }

        if (signature == null || !AuthoredSights.TryGetValue(signature, out AuthoredSight authored))
        {
            return false;
        }

        hadSight = authored.HadSight;
        sightId = authored.SightId;
        return true;
    }

    internal static bool TryAlignmentDefault(string weaponKey, out float vertical, out float forward)
    {
        vertical = 0f;
        forward = 0f;
        if (weaponKey == null || !AlignmentDefaults.TryGetValue(weaponKey, out AlignmentDefault preset))
        {
            return false;
        }

        vertical = preset.Vertical;
        forward = preset.Forward;
        return true;
    }

    private static void IndexWeapon(PlayerOfflineGearDataBlock block, string display)
    {
        string weaponKey = block.persistentID.ToString(CultureInfo.InvariantCulture);
        if (!WeaponTitles.ContainsKey(weaponKey))
        {
            WeaponTitles.Add(weaponKey, string.IsNullOrWhiteSpace(display) ? block.name : display);
        }

        string signature = GearJsonEditor.ComponentSignature(block.GearJSON);
        if (signature != null)
        {
            if (!WeaponKeys.ContainsKey(signature))
            {
                WeaponKeys.Add(signature, weaponKey);
            }

            if (!AuthoredSights.ContainsKey(signature))
            {
                bool hadSight = GearJsonEditor.TryGetComponentValue(block.GearJSON, GearJsonEditor.SightComponentId, out long sightValue)
                    && sightValue >= 0
                    && sightValue <= uint.MaxValue;
                AuthoredSights.Add(signature, new AuthoredSight
                {
                    HadSight = hadSight,
                    SightId = hadSight ? (uint)sightValue : 0u
                });
            }
        }

        RememberAlignmentDefault(weaponKey, block.name, display);
    }

    private static void RememberAlignmentDefault(string weaponKey, string datablockName, string display)
    {
        if (AlignmentDefaults.ContainsKey(weaponKey) || !TryAlignmentPreset(datablockName, out AlignmentDefault preset))
        {
            return;
        }

        AlignmentDefaults.Add(weaponKey, preset);
        string title = string.IsNullOrWhiteSpace(display) ? datablockName : display;
        Plugin.LogSource.LogInfo(
            "Default alignment for " + title
            + ": scope vertical " + preset.Vertical.ToString("+0.0000;-0.0000", CultureInfo.InvariantCulture)
            + ", scope forward " + preset.Forward.ToString("+0.0000;-0.0000", CultureInfo.InvariantCulture) + ".");
    }

    private static bool TryAlignmentPreset(string datablockName, out AlignmentDefault preset)
    {
        if (datablockName != null && AlignmentPresets.TryGetValue(datablockName, out preset))
        {
            return true;
        }

        preset = default;
        return false;
    }

    private static string Signature(GearIDRange range)
    {
        if (Components == null)
        {
            Components = (eGearComponent[])Enum.GetValues(typeof(eGearComponent));
        }

        Dictionary<int, long> values = new Dictionary<int, long>();
        for (int i = 0; i < Components.Length; i++)
        {
            eGearComponent component = Components[i];
            if (component == eGearComponent.None || component == eGearComponent.SightPart)
            {
                continue;
            }

            uint value = range.GetCompID(component);
            if (value == 0)
            {
                continue;
            }

            values[(int)component] = value;
        }

        return GearJsonEditor.FormatSignature(values);
    }

    private static string SanitizeKey(string name)
    {
        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            bool reserved = character switch
            {
                ' ' or '\t' or '\n' or '\r' or '[' or ']' or '=' or '#' => true,
                _ => false
            };
            builder.Append(reserved ? '_' : character);
        }

        string text = builder.ToString().Trim('_');
        return text.Length == 0 ? "weapon" : text;
    }

    private static bool IsFrontIronSight(string name)
    {
        const string prefix = "IronSights_";
        if (string.IsNullOrEmpty(name) || name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string body = name.Substring(prefix.Length);
        const string copy = "_Copy";
        while (body.EndsWith(copy, StringComparison.OrdinalIgnoreCase))
        {
            body = body.Substring(0, body.Length - copy.Length);
        }

        return body.EndsWith("_F", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsUnusableScope(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (HiddenNames.Contains(name))
        {
            return true;
        }

        int hash = name.IndexOf('#');
        if (hash >= 0)
        {
            name = name.Substring(0, hash);
        }

        const string copy = "_Copy";
        while (name.EndsWith(copy, StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - copy.Length);
        }

        if (HiddenNames.Contains(name) || string.Equals(name, "Sight_11", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string compact = CompactLabel(name);
        return compact == "IRONSIGHTS2B" || compact == "IRONSIGHTS11";
    }

    private static bool IsHiddenScope(GearSightPartDataBlock block)
    {
        if (!IsUnusableScope(block.name) && !IsUnusableScope(TitleOf(block, block.name)))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(block.name))
        {
            HiddenNames.Add(block.name);
        }

        return true;
    }

    private static string CompactLabel(string name)
    {
        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            if (character == ' ' || character == '_' || character == '#')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    internal static bool AppliesTo(GearIDRange range)
    {
        if (range == null || ExcludedSignatures.Count == 0)
        {
            return range != null;
        }

        string signature;
        try
        {
            signature = Signature(range);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Weapon signature: " + ex.Message);
            return true;
        }

        return signature == null || !ExcludedSignatures.Contains(signature);
    }

    private static void RememberRevolver(PlayerOfflineGearDataBlock block)
    {
        string signature = GearJsonEditor.ComponentSignature(block.GearJSON);
        if (signature == null)
        {
            Plugin.LogSource.LogWarning("Could not identify " + block.name + ". Sight selection was not blocked for it.");
            return;
        }

        ExcludedSignatures.Add(signature);
    }

    private static void DropRevolverSettings(List<string> revolvers)
    {
        if (revolvers.Count == 0)
        {
            return;
        }

        bool save = Plugin.Instance.Config.SaveOnConfigSet;
        Plugin.Instance.Config.SaveOnConfigSet = false;
        try
        {
            for (int i = 0; i < revolvers.Count; i++)
            {
                ConfigDefinition definition = new ConfigDefinition(revolvers[i], "Sight");
                Plugin.Instance.Config.Bind(definition, GearJsonEditor.DefaultSight);
                Plugin.Instance.Config.Remove(definition);
            }
        }
        finally
        {
            Plugin.Instance.Config.SaveOnConfigSet = save;
        }
    }

    private static bool IsRevolver(string name)
    {
        return !string.IsNullOrEmpty(name) && name.IndexOf("Revolver", StringComparison.OrdinalIgnoreCase) >= 0;
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

        return TitleOf(Find(sightName), sightName);
    }

    private static string TitleOf(GearSightPartDataBlock block, string fallback)
    {
        string publicName = block?.General?.PublicName;
        if (!string.IsNullOrWhiteSpace(publicName))
        {
            string localized = Localize(publicName.Trim());
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized.IndexOf('_') >= 0 && localized.IndexOf(' ') < 0 ? Prettify(localized) : localized;
            }
        }

        string source = block != null && !string.IsNullOrWhiteSpace(block.name) ? block.name : fallback;
        return Prettify(source);
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

        builder.Append('\n').Append("Updates the equipped weapon and its icon.");
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

    private struct AuthoredSight
    {
        public bool HadSight;

        public uint SightId;
    }

    private struct AlignmentDefault
    {
        public float Vertical;

        public float Forward;
    }
}
