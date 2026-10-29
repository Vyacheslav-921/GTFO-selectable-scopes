using BepInEx.Configuration;
using GameData;

namespace SelectableScopes;

internal static class ScopeApplier
{
    private static readonly HashSet<string> ReportedChoices = new HashSet<string>(StringComparer.Ordinal);

    private static readonly HashSet<uint> ReportedFailures = new HashSet<uint>();

    internal static bool IsWriting { get; private set; }

    internal static void ApplyConfigured()
    {
        if (IsWriting || !Plugin.Ready || !Plugin.Enabled.Value || ScopeCatalog.Weapons == null)
        {
            return;
        }

        foreach (KeyValuePair<uint, ConfigEntry<string>> weapon in ScopeCatalog.Weapons)
        {
            if (!TryRewrite(weapon.Key, null, out PlayerOfflineGearDataBlock block, out string updated, out string choice))
            {
                continue;
            }

            IsWriting = true;
            try
            {
                block.GearJSON = updated;
            }
            finally
            {
                IsWriting = false;
            }

            Plugin.LogSource.LogInfo(block.name + " sight set to " + choice + ".");
        }
    }

    private static bool TryRewrite(uint persistentId, string gearJson, out PlayerOfflineGearDataBlock block, out string updated, out string choice)
    {
        block = null;
        updated = gearJson;
        choice = null;
        if (ScopeCatalog.Weapons == null || !ScopeCatalog.Weapons.TryGetValue(persistentId, out ConfigEntry<string> entry))
        {
            return false;
        }

        choice = entry.Value;
        if (string.IsNullOrEmpty(choice) || string.Equals(choice, GearJsonEditor.DefaultSight, StringComparison.Ordinal))
        {
            return false;
        }

        if (!ScopeCatalog.Sights.TryGetValue(choice, out uint sightId))
        {
            if (ReportedChoices.Add(persistentId + ":" + choice))
            {
                Plugin.LogSource.LogWarning("Unknown sight '" + choice + "' for gear " + persistentId + ". Leaving that weapon unchanged.");
            }

            return false;
        }

        if (gearJson == null)
        {
            try
            {
                block = GameDataBlockBase<PlayerOfflineGearDataBlock>.GetBlock(persistentId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug("Gear " + persistentId + " was not readable: " + ex.Message);
                return false;
            }

            if (block == null)
            {
                return false;
            }

            gearJson = block.GearJSON;
        }

        SightEditResult result = GearJsonEditor.TrySetSight(gearJson, sightId, out updated);
        if (result == SightEditResult.Failed && ReportedFailures.Add(persistentId))
        {
            Plugin.LogSource.LogWarning("Could not write a sight component on gear " + persistentId + ".");
        }

        return result == SightEditResult.Updated;
    }
}
