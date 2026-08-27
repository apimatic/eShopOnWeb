using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>Shape of a Twilio API error body (code / message / more_info / status).</summary>
internal sealed class TwilioErrorDto
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
