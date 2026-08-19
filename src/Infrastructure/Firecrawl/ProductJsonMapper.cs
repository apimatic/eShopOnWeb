using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Maps the untyped JSON that a Firecrawl extract job returns (shaped by our request schema) into
/// <see cref="SupplierProduct"/> records. The shape is not guaranteed by the SDK, so every field is
/// read defensively and missing/mismatched values fall back to null rather than throwing.
/// </summary>
internal static class ProductJsonMapper
{
    public static IReadOnlyList<SupplierProduct> Map(object? data)
    {
        var results = new List<SupplierProduct>();
        if (data is not JsonElement root)
        {
            // System.Text.Json deserializes an `object` property to JsonElement; anything else is unusable.
            return results;
        }

        var array = LocateProductsArray(root);
        if (array is null)
        {
            return results;
        }

        // Dedup by the product's stable key (falling back to name) so a product the extractor happens
        // to list twice is counted — and imported — once.
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var product = MapProduct(element);
            var dedupKey = product.ProductKey ?? product.Name;
            if (!string.IsNullOrWhiteSpace(dedupKey) && !seen.Add(dedupKey.Trim().ToLowerInvariant()))
            {
                continue;
            }

            results.Add(product);
        }

        return results;
    }

    private static JsonElement? LocateProductsArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            // Preferred: our schema's top-level `products` array.
            if (TryGetProperty(root, "products", out var products) && products.ValueKind == JsonValueKind.Array)
            {
                return products;
            }

            // Some responses nest the extracted payload under `data`.
            if (TryGetProperty(root, "data", out var nested))
            {
                return LocateProductsArray(nested);
            }
        }

        return null;
    }

    private static SupplierProduct MapProduct(JsonElement element)
    {
        var name = GetString(element, "name", "title", "productName");
        var description = GetString(element, "description", "summary");
        var brand = GetString(element, "brand", "manufacturer", "vendor");
        var key = GetString(element, "url", "productUrl", "link", "id", "sku");
        var price = GetDecimal(element, "price", "amount", "cost");

        return new SupplierProduct(name, description, price, brand, key);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        // Firecrawl echoes our schema keys, but be lenient about casing just in case.
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value))
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        var s = value.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            return s;
                        }
                        break;
                    case JsonValueKind.Number:
                        return value.GetRawText();
                }
            }
        }
        return null;
    }

    private static decimal? GetDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetDecimal(out var number):
                    return number;
                case JsonValueKind.String:
                    var parsed = ParsePrice(value.GetString());
                    if (parsed.HasValue)
                    {
                        return parsed;
                    }
                    break;
            }
        }
        return null;
    }

    private static decimal? ParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Strip currency symbols/labels and thousands separators, keep digits, sign and decimal point.
        var builder = new StringBuilder(raw!.Length);
        foreach (var ch in raw)
        {
            if (char.IsDigit(ch) || ch == '.' || ch == '-')
            {
                builder.Append(ch);
            }
        }

        var cleaned = builder.ToString();
        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return null;
    }
}
