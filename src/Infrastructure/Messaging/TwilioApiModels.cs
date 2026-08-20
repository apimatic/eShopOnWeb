using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }
}

internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }
}

internal sealed class TwilioListMessageResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResource>? Messages { get; set; }

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

internal sealed class TwilioRestError
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
