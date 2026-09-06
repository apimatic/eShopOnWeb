using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializer settings shared by every Maxio request and response.
/// </summary>
internal static class MaxioJson
{
    /// <summary>
    /// Maxio speaks snake_case throughout, so a single naming policy replaces per-property
    /// attributes on the wire models. Nulls are dropped on the way out because Maxio treats an
    /// explicit <c>null</c> as "clear this field" for some attributes.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
