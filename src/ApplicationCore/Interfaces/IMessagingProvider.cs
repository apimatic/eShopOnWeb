using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio). Implementations convert all provider failures —
/// API errors, unreadable responses and connection failures — into
/// <see cref="Exceptions.MessagingProviderException"/> and never log phone numbers or secrets.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>
    /// Asks the provider whether the number is a usable destination.
    /// Returns null when the provider says the number is not valid.
    /// </summary>
    Task<VerifiedPhoneNumber?> VerifyPhoneNumberAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct);

    /// <summary>Queues a message with the provider to be sent at a later time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Cancels a message the provider has not sent yet.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageId, CancellationToken ct);

    /// <summary>Pulls the provider's current record of a message (no webhooks exist).</summary>
    Task<ProviderMessage> FetchMessageAsync(string providerMessageId, CancellationToken ct);

    /// <summary>
    /// Disposes of the message text at the provider. The message record and its
    /// outcome survive; the body is no longer retrievable.
    /// </summary>
    Task RedactMessageBodyAsync(string providerMessageId, CancellationToken ct);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured
    /// sending number within the given date-sent range, covering the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
