using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Maxio's JSON payloads use snake_case field names throughout the spec (e.g. first_name,
/// product_handle). JsonNamingPolicy.SnakeCaseLower lets the wire model properties stay
/// ordinary PascalCase C# and still line up with the contract with no per-property attributes.
/// </summary>
internal static class MaxioJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
