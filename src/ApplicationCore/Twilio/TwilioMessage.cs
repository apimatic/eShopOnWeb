using System;

namespace Microsoft.eShopWeb.ApplicationCore.Twilio;

public sealed class TwilioMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public string? Direction { get; init; }
    public string? MessagingServiceSid { get; init; }
}

public sealed class TwilioLookupResult
{
    public bool Valid { get; init; }
    public string? PhoneNumber { get; init; }
    public string[] ValidationErrors { get; init; } = Array.Empty<string>();
}

public sealed class CreateTwilioMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public string? From { get; init; }
    public string? MessagingServiceSid { get; init; }
    public string? ScheduleType { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}
