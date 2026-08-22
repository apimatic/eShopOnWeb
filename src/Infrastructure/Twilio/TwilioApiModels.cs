using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class TwilioMessageResource
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }

    [JsonConverter(typeof(Rfc2822DateTimeOffsetConverter))]
    public DateTimeOffset? DateCreated { get; set; }

    [JsonConverter(typeof(Rfc2822DateTimeOffsetConverter))]
    public DateTimeOffset? DateSent { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MessagingServiceSid { get; set; }
}

internal sealed class TwilioMessageListResponse
{
    public TwilioMessageResource[]? Messages { get; set; }
    public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupResponse
{
    public bool Valid { get; set; }
    public string? PhoneNumber { get; set; }
    public string[]? ValidationErrors { get; set; }
}

internal sealed class TwilioApiError
{
    public int? Code { get; set; }
    public int? Status { get; set; }
}
