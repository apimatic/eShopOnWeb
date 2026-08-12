using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The shop's view of the SMS provider (Twilio). Everything the notification feature needs to say
/// to, or ask of, the provider goes through this seam so the application core never depends on a
/// concrete client. The implementation lives in Infrastructure.
/// </summary>
public interface ISmsNotificationGateway
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, for its canonical
    /// E.164 form. Used to reject unusable numbers at registration rather than at send time.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateDestinationAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now. Returns the provider's identifier and initial status.</summary>
    Task<MessageDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> — held by the
    /// provider, not by this application. Returns the provider's identifier and initial status.
    /// </summary>
    Task<MessageDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message previously scheduled with the provider, before it goes out.</summary>
    Task CancelScheduledAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>Re-read the current, authoritative delivery outcome of one message by its SID.</summary>
    Task<MessageDispatchResult> FetchAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider so its text is no longer retrievable there,
    /// while the message record and its delivery outcome survive.
    /// </summary>
    Task DisposeContentAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from the configured sending number within a
    /// date range, for reconciliation. Covers the whole range (all pages).
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
