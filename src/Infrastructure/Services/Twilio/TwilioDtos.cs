using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal sealed class TwilioMessageDto
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; set; }

    [JsonPropertyName("messaging_service_sid")]
    public string? MessagingServiceSid { get; set; }
}

internal sealed class TwilioMessageListDto
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageDto> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupDto
{
    [JsonPropertyName("calling_country_code")]
    public string? CallingCountryCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("national_format")]
    public string? NationalFormat { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}
