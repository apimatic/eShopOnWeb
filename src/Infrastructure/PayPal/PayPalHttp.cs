using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Shared constants and JSON options for talking to PayPal.</summary>
internal static class PayPalHttp
{
    /// <summary>Named client used only for the OAuth token request.</summary>
    public const string TokenClientName = "PayPalToken";

    /// <summary>Named/typed client used for all PayPal API calls.</summary>
    public const string ApiClientName = "PayPalApi";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
