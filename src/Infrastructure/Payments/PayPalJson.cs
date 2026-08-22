using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
