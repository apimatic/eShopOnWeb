using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The subset of the Twilio 2010-04-01 <c>message</c> resource this integration consumes.
/// </summary>
public class TwilioMessageResource
{
    /// <summary>The provider's unique message identifier (SID).</summary>
    public string Sid { get; init; } = string.Empty;

    /// <summary>Raw provider status (queued, sending, sent, delivered, undelivered, failed, scheduled, canceled, ...).</summary>
    public string? Status { get; init; }

    public string? To { get; init; }
    public string? From { get; init; }

    /// <summary>Message text as the provider currently holds it (empty once redacted).</summary>
    public string? Body { get; init; }

    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }

    public string? MessagingServiceSid { get; init; }
}
