using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Json options used for the Maxio wire format. Maxio Advanced Billing uses
/// snake_case JSON (per its OpenAPI spec); .NET 8+ maps PascalCase members to
/// snake_case automatically with <see cref="JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}
