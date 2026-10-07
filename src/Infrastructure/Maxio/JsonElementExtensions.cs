using System;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    public static string RequireString(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new MaxioApiException(200, null, $"Expected string property '{name}' in Maxio response.");
        }

        return value.GetString()!;
    }

    public static long RequireInt64(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var parsed))
        {
            throw new MaxioApiException(200, null, $"Expected numeric property '{name}' in Maxio response.");
        }

        return parsed;
    }
}
