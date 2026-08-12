using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The messaging capability of the provider: send now, schedule for later, cancel a not-yet-sent
/// message, read a message's current outcome, dispose of a message's content, and list the
/// provider's own record of messages for reconciliation.
/// </summary>
public interface ISmsGateway
{
    /// <summary>Sends a message immediately. Returns the provider's message record (identifier + initial status).</summary>
    Task<SmsMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<SmsMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current view of a message (delivery outcome, error, etc.).</summary>
    Task<SmsMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of the message's text content at the provider; the record itself survives.</summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's record of messages sent from this application's own configured sending
    /// number within a date-time range, for reconciliation. Covers the whole range.
    /// </summary>
    Task<IReadOnlyList<SmsMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
