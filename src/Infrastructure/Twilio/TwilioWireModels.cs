using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

// Wire shapes for the Twilio API responses this integration consumes. Field names and types come from
// the Twilio OpenAPI spec (api.v2010.account.message, ListMessageResponse, the Lookups v2 phone number
// resource, and the standard error model).

internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
}

internal sealed class TwilioListMessagesResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("valid")] public bool? Valid { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}

internal sealed class TwilioErrorResponse
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}
