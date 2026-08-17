using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shop's sole gateway to the messaging provider. Every provider interaction goes through here,
/// so that provider details (hosts, encoding, auth) stay in one place and the rest of the app deals
/// only in domain terms.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// The provider's configured sending number — the only "from" reconciliation counts against.
    /// </summary>
    string ConfiguredSenderNumber { get; }

    /// <summary>
    /// Validates and canonicalises a number. Returns whether the provider considers it a usable
    /// destination and, when valid, the canonical E.164 form to store.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates one outbound message. When <see cref="SmsSendCommand.SendAt"/> is set the message is
    /// scheduled with the provider to be sent later, rather than immediately.
    /// </summary>
    Task<SmsSendResult> SendAsync(SmsSendCommand command, CancellationToken cancellationToken = default);

    /// <summary>Reads back the provider's current record of one message by its id.</summary>
    Task<SmsMessageState?> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="fromNumber"/> within the
    /// date range, following pagination to cover the whole range.
    /// </summary>
    Task<IReadOnlyList<SmsMessageState>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so its text is no longer retrievable there.</summary>
    Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a not-yet-sent (scheduled) message at the provider. Returns true if it was cancelled.
    /// </summary>
    Task<bool> CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default);
}
