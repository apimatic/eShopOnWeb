using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider boundary. All delivery outcomes are obtained by asking the
/// provider (the app has no publicly reachable URL, so the provider cannot call back).
/// </summary>
public interface ITextMessagingService
{
    /// <summary>Asks the provider whether the number is a usable destination and for its canonical form.</summary>
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a text message immediately from the application's configured sending number.</summary>
    Task<SentTextMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider itself for a later instant (provider-side scheduling).</summary>
    Task<SentTextMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-scheduled message that has not yet gone out.</summary>
    Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Polls the provider for a message's current delivery outcome.</summary>
    Task<TextMessageDeliveryOutcome> GetDeliveryOutcomeAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider; the record and its outcome survive.</summary>
    Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the range (the range restriction is applied by the provider).
    /// </summary>
    Task<ProviderMessageListResult> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
