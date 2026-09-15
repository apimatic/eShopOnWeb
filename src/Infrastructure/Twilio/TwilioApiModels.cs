using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// JSON shapes deserialized from the Twilio API, matching the OpenAPI contract in <c>api-specs/</c>.
/// Only the fields this integration needs are declared.
/// </summary>
internal sealed class TwilioMessageResource
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
}

internal sealed class TwilioMessageListResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResource> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}

/// <summary>The provider's standard error envelope (Twilio API error response).</summary>
internal sealed class TwilioErrorResponse
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("more_info")]
    public string? MoreInfo { get; set; }
}
