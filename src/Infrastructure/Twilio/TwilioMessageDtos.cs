using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The Message resource as returned by the Twilio 2010-04-01 API (snake_case). Only the fields this
/// integration needs are mapped; the shape mirrors <c>api.v2010.account.message</c> in the spec.
/// </summary>
internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
}

/// <summary>The paged list envelope (<c>ListMessageResponse</c> in the spec).</summary>
internal sealed class TwilioMessageListResponse
{
    [JsonPropertyName("messages")] public TwilioMessageResource[]? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>The canonical Twilio error model (<c>{ code, message, more_info, status }</c>).</summary>
internal sealed class TwilioErrorResponse
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}
