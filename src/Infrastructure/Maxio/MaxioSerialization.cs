using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Serializer settings shared by every Maxio request and response.</summary>
internal static class MaxioSerialization
{
    /// <summary>
    /// Wire property names are declared per-property on the models, so no naming policy is applied.
    /// Nulls are omitted on write: Maxio treats an explicit null as "set this field to null", which
    /// is not what an unspecified optional attribute means.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
