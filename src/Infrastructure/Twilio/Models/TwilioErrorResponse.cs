using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio.Models;

/// <summary>Twilio's standard error body, returned on a 4xx/5xx from any API.</summary>
public class TwilioErrorResponse
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("more_info")]
    public string? MoreInfo { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
