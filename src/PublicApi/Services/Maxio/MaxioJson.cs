using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
