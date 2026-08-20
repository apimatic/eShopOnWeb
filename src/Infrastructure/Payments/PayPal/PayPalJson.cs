using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

internal static class PayPalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    public static JsonContent Content(object value) =>
        JsonContent.Create(value, options: Options);
}
