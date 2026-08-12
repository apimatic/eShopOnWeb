using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The SMS provider seam. Everything the integration needs to say to, or ask of, the messaging
/// provider goes through this interface; the concrete implementation talks the provider's HTTP API.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider whether <paramref name="rawNumber"/> is a usable destination and, if so,
    /// what its canonical form is. Used to reject unusable numbers at registration time rather than
    /// at send time.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends (or schedules) a message and returns the provider's view of the created message.</summary>
    Task<ProviderMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads a single message back from the provider by its id, for its current delivery outcome.</summary>
    Task<ProviderMessage> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the body text of a message at the provider so it can no longer be retrieved there,
    /// while the record of the message and its outcome survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number within a date range. The provider is asked for that sender's messages directly rather
    /// than filtering a wider answer after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
