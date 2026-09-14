using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialization settings for the Maxio wire format.
/// </summary>
public static class MaxioJson
{
    /// <summary>
    /// Every contract member is mapped with an explicit <see cref="JsonPropertyNameAttribute"/>, so
    /// no naming policy is configured: the mapping to the specification's snake_case members stays
    /// visible on the contract itself. Nulls are dropped on write because Maxio distinguishes an
    /// omitted attribute from one explicitly set to null on several create operations.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
