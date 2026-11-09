using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Small helpers for reading the untyped (JsonElement) answers the Stax SDK returns.</summary>
public static class Json
{
    public static JsonElement ToElement(object? value)
    {
        if (value is JsonElement e) return e;
        return JsonSerializer.SerializeToElement(value);
    }

    public static JsonElement? Get(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.Null ? null : p.Value;
        }
        return null;
    }

    public static string? Str(this JsonElement e, string name)
    {
        var v = e.Get(name);
        if (v is null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.String => v.Value.GetString(),
            JsonValueKind.Number => v.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static decimal? Dec(this JsonElement e, string name)
    {
        var v = e.Get(name);
        if (v is null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetDecimal(out var d)) return d;
        if (v.Value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    public static bool? Bool(this JsonElement e, string name)
    {
        var v = e.Get(name);
        if (v is null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.Value.TryGetInt32(out var i) ? i != 0 : null,
            JsonValueKind.String => bool.TryParse(v.Value.GetString(), out var b) ? b : v.Value.GetString() == "1",
            _ => null
        };
    }

    public static Guid? GuidOf(this JsonElement e, string name) =>
        Guid.TryParse(e.Str(name), out var g) ? g : null;

    /// <summary>Stax timestamps ("yyyy-MM-dd HH:mm:ss") are UTC.</summary>
    public static DateTime? Time(this JsonElement e, string name)
    {
        var s = e.Str(name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? DateTime.SpecifyKind(t, DateTimeKind.Utc)
            : null;
    }

    /// <summary>Returns the "data" array of a Stax paginated answer, or the array itself.</summary>
    public static JsonElement[] Items(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array) return e.EnumerateArray().ToArray();
        var data = e.Get("data");
        if (data is { ValueKind: JsonValueKind.Array }) return data.Value.EnumerateArray().ToArray();
        return Array.Empty<JsonElement>();
    }
}
