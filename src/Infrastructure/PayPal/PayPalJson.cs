using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Shared JSON settings for PayPal payloads. PayPal uses snake_case field names, so DTO properties are written
/// in PascalCase and mapped via <see cref="JsonNamingPolicy.SnakeCaseLower"/>; null request fields are omitted.
/// </summary>
internal static class PayPalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
