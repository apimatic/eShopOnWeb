using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// JSON settings shared by every Maxio call. The API uses snake_case member names throughout, so the
/// contracts stay idiomatic C# and the naming policy does the translation.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Maxio rejects nothing for being absent, but does interpret explicit nulls; only send what we mean.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
