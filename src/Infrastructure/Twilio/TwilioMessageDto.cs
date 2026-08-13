using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The provider's Message resource, as defined by the Twilio api.v2010 OpenAPI spec
/// (schema <c>api.v2010.account.message</c>). Only the fields this integration consumes are mapped.
/// Note the spec's types: <c>error_code</c> is an integer; the <c>date_*</c> fields are RFC-2822 strings.
/// </summary>
internal sealed class TwilioMessageDto
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("messaging_service_sid")]
    public string? MessagingServiceSid { get; set; }
}

/// <summary>Paging envelope for a list of messages (spec schema <c>ListMessageResponse</c>).</summary>
internal sealed class TwilioMessageListDto
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageDto> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

/// <summary>The provider's error model returned on a non-2xx response.</summary>
internal sealed class TwilioErrorDto
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("more_info")]
    public string? MoreInfo { get; set; }
}

/// <summary>The provider's phone-number lookup response (Lookups v2 schema <c>LookupResponse</c>).</summary>
internal sealed class TwilioLookupDto
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}
