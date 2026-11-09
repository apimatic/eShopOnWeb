using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public static class JsonNodeExtensions
{
    public static string? Str(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v is null) return null;
        return v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v.ToString();
    }

    public static decimal Dec(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v is null) return 0m;
        return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    public static bool Bool(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v is null) return false;
        var s = v.ToString();
        return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stax returns either a bare array or a paginated { data: [...] } wrapper.</summary>
    public static JsonNode[] Items(this JsonNode? node)
    {
        var arr = node as JsonArray ?? node?["data"] as JsonArray;
        return arr?.Where(n => n is not null).Select(n => n!).ToArray() ?? Array.Empty<JsonNode>();
    }
}
