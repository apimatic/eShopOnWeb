using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the SMS provider (Twilio). The concrete implementation lives in Infrastructure and is
/// the only place that knows the provider's HTTP shape, credentials, sending number and messaging
/// service. It sends, reads, schedules, cancels, redacts and lists messages — nothing about order
/// or contact-number domain logic leaks in here.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Asks the provider whether <paramref name="phoneNumber"/> is a usable destination and, if so,
    /// returns its canonical E.164 form. Used to reject bad numbers at registration time.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/> (a few days out).
    /// The provider holds the schedule; nothing in this application waits on a timer.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message so its delivery outcome can be refreshed.</summary>
    Task<ProviderMessage?> FetchAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never reaches the shopper.</summary>
    Task CancelScheduledAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable there, while the
    /// message resource — and thus the fact it was sent and its outcome — survives.
    /// </summary>
    Task RedactBodyAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages the application's configured sending number sent in
    /// the given range, asking the provider to filter by that number rather than filtering afterwards.
    /// Covers the whole range (following pagination).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>The application's own configured sending number (<c>Twilio:FromNumber</c>), for reporting.</summary>
    string SendingNumber { get; }
}
