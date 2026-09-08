using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Shared JSON options for talking to the Maxio API. Maxio uses snake_case for both
/// request and response field names; responses are case-insensitive on read.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true
    };
}
