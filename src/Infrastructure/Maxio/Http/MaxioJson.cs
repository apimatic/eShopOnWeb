using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Serialization settings shared by every Maxio request and response.
/// </summary>
internal static class MaxioJson
{
    /// <summary>
    /// Maxio uses snake_case property names throughout. Every transcribed model still declares its
    /// wire name explicitly with <see cref="JsonPropertyNameAttribute"/>, so the naming policy here
    /// is a safety net rather than the contract.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Maxio rejects nulls for several optional attributes, so omit anything unset rather than
        // sending an explicit null.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Provider-side additions must never break a running deployment.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}
