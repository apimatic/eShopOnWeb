using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}

internal sealed class TwilioMessageResponse
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    public object? ErrorCode { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }
}

internal sealed class TwilioMessageListResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResponse> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}
