using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// A snapshot of a provider message resource, as returned when the message is created, scheduled,
/// fetched or updated.
/// </summary>
public class SmsMessageResult
{
    /// <summary>The provider's message identifier (message SID).</summary>
    public string Sid { get; init; } = string.Empty;

    /// <summary>The provider's status for the message (e.g. queued, scheduled, sent, delivered, undelivered, failed, canceled).</summary>
    public string Status { get; init; } = string.Empty;

    public int? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>When the message is scheduled, the time the provider will send it.</summary>
    public DateTimeOffset? ScheduledFor { get; init; }
}
