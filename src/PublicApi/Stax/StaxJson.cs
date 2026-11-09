using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Helpers for reading the loosely typed (<c>object</c>) payloads the Stax SDK returns.</summary>
public static class StaxJson
{
    public static JsonElement ToElement(object? value)
    {
        if (value is JsonElement element)
        {
            return element.Clone();
        }

        if (value is null)
        {
            return default;
        }

        return JsonSerializer.SerializeToElement(value);
    }

    public static JsonElement? Prop(this JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined)
        {
            return value;
        }

        return null;
    }

    public static string? Str(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.Value.ToString(),
            _ => null
        };
    }

    public static decimal? Dec(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        if (value is null)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDecimal(out var d))
        {
            return d;
        }

        if (value.Value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d))
        {
            return d;
        }

        return null;
    }

    public static bool? Bool(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.Value.TryGetInt32(out var i) ? i != 0 : null,
            JsonValueKind.String => bool.TryParse(value.Value.GetString(), out var b) ? b : null,
            _ => null
        };
    }

    /// <summary>Stax timestamps look like <c>2026-11-09 16:55:01</c> and are UTC.</summary>
    public static DateTimeOffset? Date(this JsonElement element, string name)
    {
        var text = element.Str(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static IEnumerable<JsonElement> Items(this JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object && element.Prop("data") is { ValueKind: JsonValueKind.Array } data)
        {
            foreach (var item in data.EnumerateArray())
            {
                yield return item;
            }
        }
    }
}
