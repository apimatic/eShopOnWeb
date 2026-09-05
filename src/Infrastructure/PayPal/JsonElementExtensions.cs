using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Null-tolerant readers over PayPal's JSON, so the adapter can react to a response without caring
/// which optional fields it happens to carry. They are named apart from <c>JsonElement</c>'s own
/// members because the built-in ones throw on a missing field.
/// </summary>
internal static class JsonElementExtensions
{
    public static JsonElement? Prop(this JsonElement? element, string name)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.Value.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value;
    }

    public static JsonElement? Prop(this JsonElement element, string name) => Prop((JsonElement?)element, name);

    public static string? Text(this JsonElement? element, string name)
    {
        var value = element.Prop(name);
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null
        };
    }

    public static string? Text(this JsonElement element, string name) => Text((JsonElement?)element, name);

    public static DateTimeOffset? Instant(this JsonElement? element, string name)
    {
        var text = element.Text(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    public static DateTimeOffset? Instant(this JsonElement element, string name) => Instant((JsonElement?)element, name);

    public static decimal MoneyValue(this JsonElement? element, string name = "amount")
    {
        var text = element.Prop(name).Text("value");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    public static decimal MoneyValue(this JsonElement element, string name = "amount")
        => MoneyValue((JsonElement?)element, name);

    /// <summary>
    /// Reads the <c>value</c> of a money object that <paramref name="element"/> itself is, rather than a
    /// money object held under a field name.
    /// </summary>
    public static decimal MoneyValueHere(this JsonElement? element)
    {
        var text = element.Text("value");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    public static decimal MoneyValueHere(this JsonElement element) => MoneyValueHere((JsonElement?)element);

    public static string? MoneyCurrency(this JsonElement? element, string name = "amount")
        => element.Prop(name).Text("currency_code");

    public static string? MoneyCurrency(this JsonElement element, string name = "amount")
        => MoneyCurrency((JsonElement?)element, name);

    /// <summary>Walks a field that may be missing, null, or not an array, without throwing.</summary>
    public static IEnumerable<JsonElement> ArrayOrEmpty(this JsonElement? element)
        => element is { } value && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    public static IEnumerable<JsonElement> ArrayOrEmpty(this JsonElement element) => ArrayOrEmpty((JsonElement?)element);
}
