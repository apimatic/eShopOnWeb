using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A request to send an SMS. When <see cref="SendAt"/> is set the message is scheduled with the
/// provider (queued on their side) rather than sent immediately.
/// </summary>
public class SmsSendRequest
{
    public SmsSendRequest(string to, string body, DateTimeOffset? sendAt = null)
    {
        To = to;
        Body = body;
        SendAt = sendAt;
    }

    /// <summary>Destination number in E.164.</summary>
    public string To { get; }

    /// <summary>Message text.</summary>
    public string Body { get; }

    /// <summary>When set, the provider schedules delivery for this instant (a "queued with the
    /// provider" send). When null the message is sent immediately.</summary>
    public DateTimeOffset? SendAt { get; }
}

/// <summary>
/// The provider's own view of a message: its identifier and current delivery outcome. This is the
/// state the provider owns that a later request can act on and report against.
/// </summary>
public class SmsMessageState
{
    /// <summary>The provider message identifier (Twilio <c>sid</c>).</summary>
    public string Sid { get; set; } = string.Empty;

    /// <summary>Raw provider status (e.g. queued, sending, sent, delivered, undelivered, failed,
    /// scheduled, canceled).</summary>
    public string? Status { get; set; }

    /// <summary>Provider error code when a message failed, if any.</summary>
    public int? ErrorCode { get; set; }

    /// <summary>Provider error description when a message failed, if any.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>The sender number as recorded by the provider.</summary>
    public string? From { get; set; }

    /// <summary>The destination number as recorded by the provider.</summary>
    public string? To { get; set; }

    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

/// <summary>Result of validating a phone number with the provider's lookup capability.</summary>
public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number, present when valid.</summary>
    public string? CanonicalNumber { get; set; }

    /// <summary>Provider-reported reasons the number is not usable, when invalid.</summary>
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}
