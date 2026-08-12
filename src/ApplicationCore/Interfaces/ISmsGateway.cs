using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging side of the SMS provider: sending, scheduling, reading, cancelling,
/// redacting and reconciling messages. Implemented against the provider's messaging API.
/// Every call may throw <see cref="Exceptions.SmsGatewayException"/> on a provider error;
/// callers that must not fail the underlying operation are responsible for catching it.
/// </summary>
public interface ISmsGateway
{
    /// <summary>The configured sending number (Twilio:FromNumber) this gateway sends and reconciles against.</summary>
    string SendingNumber { get; }

    /// <summary>Send a message now from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/>. Scheduling is
    /// performed by the provider (via its Messaging Service), not held in this application.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current record of a message by its identifier.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message at the provider so it never goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider (redact the body) so the text is no
    /// longer retrievable there. The record of the message and its outcome survives.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own records of messages sent from the configured sending number within
    /// the given range. The sender filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListOwnMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
