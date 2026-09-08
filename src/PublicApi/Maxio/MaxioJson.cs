using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The Maxio Advanced Billing API uses snake_case JSON for both requests and
/// responses. These serializer options are shared across the integration.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static T Deserialize<T>(string json)
        where T : class
    {
        var result = JsonSerializer.Deserialize<T>(json, Options);
        return result ?? throw new MaxioApiException(200, json, "The Maxio API returned an empty response body.");
    }
}
