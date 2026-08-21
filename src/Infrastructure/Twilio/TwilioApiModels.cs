using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Maps to api.v2010.account.message in twilio_api_v2010.
/// </summary>
public class TwilioMessageResource
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("messaging_service_sid")]
    public string? MessagingServiceSid { get; set; }

    [JsonPropertyName("account_sid")]
    public string? AccountSid { get; set; }
}

/// <summary>
/// Maps to ListMessageResponse in twilio_api_v2010.
/// </summary>
public class TwilioListMessageResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResource>? Messages { get; set; }

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; set; }
}

/// <summary>
/// Maps to LookupResponse in twilio_lookups_v2.
/// </summary>
public class TwilioLookupResponse
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

public class TwilioErrorBody
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
