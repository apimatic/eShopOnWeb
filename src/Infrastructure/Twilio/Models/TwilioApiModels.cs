using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio.Models;

public class LookupResponse
{
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("calling_country_code")]
    public string? CallingCountryCode { get; set; }

    [JsonPropertyName("national_format")]
    public string? NationalFormat { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}

public class TwilioMessageResource
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

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

    [JsonPropertyName("account_sid")]
    public string? AccountSid { get; set; }
}

public class ListMessageResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResource>? Messages { get; set; }

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

public class TwilioErrorResponse
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
