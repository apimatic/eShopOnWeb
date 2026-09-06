using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Serializer settings shared by every Maxio request and response.</summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Property names come from [JsonPropertyName] attributes that mirror the specification, so no
        // naming policy is applied. Maxio occasionally serialises numerics as strings, hence the
        // permissive number handling.
        PropertyNamingPolicy = null,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
