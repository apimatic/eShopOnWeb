using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's operations this integration relies on.
/// </summary>
public interface ISmsProviderClient
{
    /// <summary>Validates a phone number and returns the provider's canonical form of it.</summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that was queued with the provider but has not gone out yet.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider so it is no longer retrievable there.</summary>
    Task<ProviderMessage> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a single message.</summary>
    Task<ProviderMessage> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the given sending number
    /// within the given date range, covering the whole range (all pages).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
