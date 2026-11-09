using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Reads a JSON request body by property name, ignoring case, underscores and dashes,
/// so "paymentMethodId" and "payment_method_id" both work.
/// </summary>
public sealed class LooseBody
{
    private readonly Dictionary<string, JsonElement> _props = new();

    private LooseBody() { }

    public static LooseBody From(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw ApiException.BadRequest("The request body must be a JSON object.");
        }

        var body = new LooseBody();
        foreach (var p in element.EnumerateObject())
        {
            body._props[Normalize(p.Name)] = p.Value;
        }
        return body;
    }

    private static string Normalize(string name) =>
        name.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();

    public JsonElement? Raw(params string[] names)
    {
        foreach (var n in names)
        {
            if (_props.TryGetValue(Normalize(n), out var v) && v.ValueKind != JsonValueKind.Null) return v;
        }
        return null;
    }

    public string? Str(params string[] names)
    {
        var v = Raw(names);
        if (v == null) return null;
        var s = v.Value.ValueKind switch
        {
            JsonValueKind.String => v.Value.GetString(),
            JsonValueKind.Number => v.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw ApiException.BadRequest($"'{names[0]}' must be a string."),
        };
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public decimal? Dec(params string[] names)
    {
        var v = Raw(names);
        if (v == null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetDecimal(out var d)) return d;
        if (v.Value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        throw ApiException.BadRequest($"'{names[0]}' must be a number.");
    }

    public int? Int(params string[] names)
    {
        var d = Dec(names);
        if (d == null) return null;
        if (d != Math.Truncate(d.Value) || d > int.MaxValue || d < int.MinValue)
        {
            throw ApiException.BadRequest($"'{names[0]}' must be a whole number.");
        }
        return (int)d.Value;
    }
}
