using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Shared JSON settings for every Maxio request/response. The Maxio OpenAPI contract uses
/// snake_case member names, so the serializer is configured with a snake_case naming policy.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
