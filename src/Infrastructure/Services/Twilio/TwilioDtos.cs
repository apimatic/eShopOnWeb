using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Wire shapes for the Twilio responses this integration consumes. Field names and types follow the
/// OpenAPI specification in <c>api-specs</c> (the authoritative contract):
/// <c>api.v2010.account.message</c>, <c>ListMessageResponse</c> and Lookups v2 <c>LookupResponse</c>.
/// </summary>
internal sealed class TwilioMessageDto
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }
}

internal sealed class TwilioListMessagesDto
{
    [JsonPropertyName("messages")] public List<TwilioMessageDto>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupDto
{
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}

internal sealed class TwilioErrorDto
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}
