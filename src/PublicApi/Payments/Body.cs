using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Reads a JSON request body leniently: property names match case-insensitively and ignore
/// underscores/dashes, so cardNumber, card_number and CardNumber are the same field.
/// </summary>
public class Body
{
    private readonly Dictionary<string, JsonElement> _props = new();

    public JsonElement Root { get; }

    public Body(JsonElement? root)
    {
        Root = root ?? default;
        if (root is { ValueKind: JsonValueKind.Object })
            foreach (var p in root.Value.EnumerateObject())
                _props[Normalize(p.Name)] = p.Value;
    }

    public static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    public bool IsObject => Root.ValueKind == JsonValueKind.Object;

    public JsonElement? Get(params string[] names)
    {
        foreach (var n in names)
            if (_props.TryGetValue(Normalize(n), out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                return v;
        return null;
    }

    public string? Str(params string[] names)
    {
        var v = Get(names);
        if (v == null) return null;
        var s = v.Value.ValueKind switch
        {
            JsonValueKind.String => v.Value.GetString(),
            JsonValueKind.Number => v.Value.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    public decimal? Decimal(params string[] names)
    {
        var v = Get(names);
        if (v == null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetDecimal(out var d)) return d;
        if (v.Value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.Value.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var d2)) return d2;
        return null;
    }

    public int? Int(params string[] names)
    {
        var d = Decimal(names);
        if (d == null || d != decimal.Truncate(d.Value)) return null;
        return (int)d.Value;
    }

    public Body? Child(params string[] names)
    {
        var v = Get(names);
        return v is { ValueKind: JsonValueKind.Object } ? new Body(v) : null;
    }

    public List<Body> Array(params string[] names)
    {
        var v = Get(names);
        if (v is not { ValueKind: JsonValueKind.Array }) return new List<Body>();
        return v.Value.EnumerateArray().Select(e => new Body(e)).ToList();
    }
}
