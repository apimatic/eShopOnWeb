using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ProviderMessageResult
{
    /// <summary>True when the provider accepted the message (API-level success).</summary>
    public bool Accepted { get; set; }

    /// <summary>The provider's identifier for the message.</summary>
    public string? MessageSid { get; set; }

    /// <summary>The provider's current status for the message (queued, scheduled, failed, ...).</summary>
    public string? Status { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ProviderMessageRecord
{
    public string MessageSid { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

/// <summary>
/// The messaging provider's operations. Implementations must not throw for
/// provider-side rejections of a message; those are outcomes, not failures.
/// </summary>
public interface IMessageProvider
{
    Task<ProviderMessageResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at a future time.</summary>
    Task<ProviderMessageResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message that is still in the provider's scheduled state.</summary>
    Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact a message's text at the provider while keeping its record.</summary>
    Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's
    /// configured sending number within the given range. Pages through the
    /// whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
