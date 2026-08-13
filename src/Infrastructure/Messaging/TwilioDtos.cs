using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The message resource as defined by the messaging API spec (api.v2010.account.message). Only the fields the
/// integration uses are mapped.
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
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
}

/// <summary>The list-messages response envelope (ListMessageResponse) with its pagination cursor.</summary>
internal sealed class TwilioListMessagesDto
{
    [JsonPropertyName("messages")] public List<TwilioMessageDto>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>The provider error model returned on a non-success response.</summary>
internal sealed class TwilioErrorDto
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}

/// <summary>The Lookups V2 phone-number response (LookupResponse). Only validation fields are mapped.</summary>
internal sealed class TwilioLookupDto
{
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}
