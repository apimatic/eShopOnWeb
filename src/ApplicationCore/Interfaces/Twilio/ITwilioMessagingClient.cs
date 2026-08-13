using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;

/// <summary>
/// Talks to the provider's messaging API — the one this integration sends, reads and reconciles
/// messages through. Every call here honours the <c>Twilio:BaseUrl</c> override when it is set.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>Sends a message now, from the configured sending number.</summary>
    Task<TwilioMessageResource> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to go out at <paramref name="sendAt"/>. The provider,
    /// not this application, holds it until then. Requires a messaging service.
    /// </summary>
    Task<TwilioMessageResource> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record for a message (to read its delivery outcome).</summary>
    Task<TwilioMessageResource> FetchAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a scheduled message before it goes out.</summary>
    Task<TwilioMessageResource> CancelScheduledAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable there,
    /// while the record that a message was sent — and what became of it — survives.
    /// </summary>
    Task<TwilioMessageResource> RedactBodyAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="fromNumber"/> within
    /// a date range. The provider is asked for that number's messages directly (by sender), rather
    /// than filtering a wider answer after the fact. Pages are followed to cover the whole range.
    /// </summary>
    Task<IReadOnlyList<TwilioMessageResource>> ListBySenderAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
