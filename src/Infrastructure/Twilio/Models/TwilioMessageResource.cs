using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio.Models;

/// <summary>
/// Mirrors the <c>api.v2010.account.message</c> schema from the Twilio messaging OpenAPI spec — the
/// fields this integration reads back from send / fetch / list / update calls.
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

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("messaging_service_sid")]
    public string? MessagingServiceSid { get; set; }
}

/// <summary>A page of the list-messages response (<c>ListMessageResponse</c> in the spec).</summary>
public class TwilioMessageListResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResource> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}
