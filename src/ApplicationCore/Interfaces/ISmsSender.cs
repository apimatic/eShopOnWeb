using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio). Everything the integration needs
/// from the provider is expressed here so the rest of the app never speaks HTTP to it directly.
///
/// Because there is no publicly reachable URL for this application, the provider cannot call
/// back: anything the app needs to know about a message's fate is pulled from the provider via
/// <see cref="GetDeliveryStateAsync"/> / <see cref="ListSentMessagesAsync"/>, never pushed to us.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical
    /// (E.164) form. Registration is rejected up front when the provider does not consider the
    /// number usable, rather than later when a message to it fails.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Hand a message to the provider for immediate delivery.</summary>
    Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current delivery outcome for a message.</summary>
    Task<SmsDeliveryState> GetDeliveryStateAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Call off a scheduled message with the provider before it goes out.</summary>
    Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's text at the provider (redaction) so it can no longer be retrieved,
    /// while the provider's record that the message was sent and what became of it survives.
    /// </summary>
    Task RedactAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured sending
    /// number over a date range. The number filter is applied by the provider (asked for that
    /// number's messages), not by filtering a wider answer after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
