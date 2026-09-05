using System.Text;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio error bodies take several shapes depending on the endpoint (see
/// maxio-spec/components/schemas/errors/*.yaml) - a flat array of strings, a map of
/// field -> message(s), or a mix of both. This best-effort flattens whichever shape shows
/// up into a single human-readable message, falling back to the raw body if it isn't JSON.
/// </summary>
internal static class MaxioErrorParser
{
    public static string ExtractMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "(empty response body)";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                var builder = new StringBuilder();
                Flatten(errors, builder);
                if (builder.Length > 0)
                {
                    return builder.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through to returning the raw body below.
        }

        return responseBody;
    }

    private static void Flatten(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }
                builder.Append(element.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, builder);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, builder);
                }
                break;
        }
    }
}
