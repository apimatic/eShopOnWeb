using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// JSON settings for the Maxio Advanced Billing API, which uses snake_case throughout.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,

        // Never send nulls: the API treats an explicit null as "clear this field".
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
