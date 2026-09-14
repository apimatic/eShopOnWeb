using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio error bodies take several documented shapes:
///   {"errors": ["msg", "msg"]}
///   {"errors": {"customer": "can't be blank"}}
///   {"errors": {"subscription": {"base": ["msg"]}}}
/// This flattens whichever shape is present into a single human-readable string.
/// </summary>
internal static class MaxioErrorParser
{
    public static string Extract(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return "Maxio returned an empty error response.";
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return Flatten(errors);
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return rawBody.Length > 500 ? rawBody[..500] : rawBody;
    }

    private static string Flatten(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;
            case JsonValueKind.Array:
                return string.Join("; ", element.EnumerateArray().Select(Flatten));
            case JsonValueKind.Object:
                return string.Join("; ", element.EnumerateObject().Select(p => $"{p.Name}: {Flatten(p.Value)}"));
            default:
                return element.ToString();
        }
    }
}
