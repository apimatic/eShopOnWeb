using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the provider's messaging API (send, read, cancel, redact, list). Implemented
/// against the Twilio OpenAPI contract. All methods surface provider failures as exceptions; the
/// caller decides how to record them so that a failed message never fails the business operation.
/// </summary>
public interface ISmsSender
{
    /// <summary>Sends (or, when <see cref="SmsSendRequest.SendAt"/> is set, schedules) a message.
    /// Returns the provider's identifier and initial status.</summary>
    Task<SmsMessageState> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current state for a message, or null if it is unknown.</summary>
    Task<SmsMessageState?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is scheduled but has not yet been sent.</summary>
    Task<SmsMessageState> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so its text is no longer retrievable,
    /// while the record that a message was sent and its outcome survive.</summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Lists the provider's messages sent from <paramref name="fromNumber"/> within the
    /// date range. The provider is asked directly for that number's messages rather than a wider
    /// answer filtered afterwards. Pages through the whole range.</summary>
    Task<IReadOnlyList<SmsMessageState>> ListMessagesAsync(string fromNumber, DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default);
}
