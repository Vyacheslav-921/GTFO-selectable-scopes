using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SelectableScopes;

internal enum SightEditResult
{
    Unchanged,
    Updated,
    Failed
}

internal static class GearJsonEditor
{
    internal const int SightComponentId = 21;

    internal const int CategoryComponentId = 2;

    internal const string DefaultSight = "Default";

    internal static bool TryGetComponentValue(string gearJson, int componentId, out long value)
    {
        value = 0L;
        if (!TryParse(gearJson, out JsonObject root))
        {
            return false;
        }

        JsonObject components = Components(root);
        if (components == null)
        {
            return false;
        }

        foreach (JsonObject component in ComponentObjects(components))
        {
            if (ReadInt(component["c"]) != componentId)
            {
                continue;
            }

            long? componentValue = ReadLong(component["v"]);
            if (!componentValue.HasValue)
            {
                return false;
            }

            value = componentValue.Value;
            return true;
        }

        return false;
    }

    internal static string GetDisplayName(string gearJson)
    {
        if (!TryParse(gearJson, out JsonObject root))
        {
            return null;
        }

        if (root["Name"] is not JsonValue name || !name.TryGetValue<string>(out string value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal static SightEditResult TrySetSight(string gearJson, long sightId, out string updated)
    {
        updated = gearJson;
        if (!TryParse(gearJson, out JsonObject root))
        {
            return SightEditResult.Failed;
        }

        JsonObject components = Components(root);
        if (components == null)
        {
            return SightEditResult.Failed;
        }

        foreach (JsonObject component in ComponentObjects(components))
        {
            if (ReadInt(component["c"]) != SightComponentId)
            {
                continue;
            }

            if (ReadLong(component["v"]) == sightId)
            {
                return SightEditResult.Unchanged;
            }

            component["v"] = JsonValue.Create(sightId);
            updated = root.ToJsonString();
            return SightEditResult.Updated;
        }

        int length = ReadInt(components["Length"]) ?? CountComponents(components);
        if (length < 0 || length > 25)
        {
            return SightEditResult.Failed;
        }

        string propertyName = ((char)('a' + length)).ToString();
        if (components[propertyName] != null)
        {
            return SightEditResult.Failed;
        }

        components[propertyName] = new JsonObject
        {
            ["c"] = JsonValue.Create(SightComponentId),
            ["v"] = JsonValue.Create(sightId)
        };
        components["Length"] = JsonValue.Create(length + 1);
        updated = root.ToJsonString();
        return SightEditResult.Updated;
    }

    private static bool TryParse(string gearJson, out JsonObject root)
    {
        root = null;
        if (string.IsNullOrEmpty(gearJson))
        {
            return false;
        }

        try
        {
            root = JsonNode.Parse(gearJson) as JsonObject;
            return root != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonObject Components(JsonObject root)
    {
        return root["Packet"]?["Comps"] as JsonObject;
    }

    private static IEnumerable<JsonObject> ComponentObjects(JsonObject comps)
    {
        foreach (KeyValuePair<string, JsonNode> component in comps)
        {
            if (component.Value is JsonObject componentObject)
            {
                yield return componentObject;
            }
        }
    }

    private static int CountComponents(JsonObject comps)
    {
        int count = 0;
        foreach (JsonObject _ in ComponentObjects(comps))
        {
            count++;
        }

        return count;
    }

    private static int? ReadInt(JsonNode node)
    {
        long? value = ReadLong(node);
        if (!value.HasValue || value.Value < int.MinValue || value.Value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static long? ReadLong(JsonNode node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out long asLong))
        {
            return asLong;
        }

        if (value.TryGetValue<int>(out int asInt))
        {
            return asInt;
        }

        if (value.TryGetValue<bool>(out bool asBool))
        {
            return asBool ? 1L : 0L;
        }

        return null;
    }

    internal static string ComponentSignature(string gearJson)
    {
        if (!TryParse(gearJson, out JsonObject root))
        {
            return null;
        }

        JsonObject components = Components(root);
        if (components == null)
        {
            return null;
        }

        Dictionary<int, long> values = new Dictionary<int, long>();
        foreach (JsonObject component in ComponentObjects(components))
        {
            int? componentId = ReadInt(component["c"]);
            long? componentValue = ReadLong(component["v"]);
            if (!componentId.HasValue || !componentValue.HasValue || componentId.Value == SightComponentId || componentValue.Value == 0)
            {
                continue;
            }

            values[componentId.Value] = componentValue.Value;
        }

        return FormatSignature(values);
    }

    internal static string FormatSignature(Dictionary<int, long> values)
    {
        if (values == null || values.Count == 0)
        {
            return null;
        }

        List<int> ids = new List<int>(values.Keys);
        ids.Sort();
        StringBuilder builder = new StringBuilder(ids.Count * 8);
        for (int i = 0; i < ids.Count; i++)
        {
            if (i != 0)
            {
                builder.Append(';');
            }

            int id = ids[i];
            builder.Append(id.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(values[id].ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
