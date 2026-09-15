using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

internal static class PayPalHttpDefaults
{
    public const string ClientName = "PayPal";

    /// <summary>Shared serializer options. Property names are pinned per-model via [JsonPropertyName].</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
