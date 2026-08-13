using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>Shared JSON options: the provider speaks snake_case.</summary>
internal static class TwilioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };
}
