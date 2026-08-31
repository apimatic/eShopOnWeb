using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider boundary. Implementations never throw for provider or
/// transport failures - they return an outcome object - so a messaging problem can
/// never fail the business operation that triggered it.
/// </summary>
public interface IMessagingService
{
    /// <summary>Validates a phone number with the provider and returns its canonical form.</summary>
    Task<NumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<MessagingOutcome> SendMessageAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider to be sent at a future instant.</summary>
    Task<MessagingOutcome> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a provider-queued message that has not yet gone out.</summary>
    Task<MessagingOutcome> CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current record of one message. Null when it cannot be read.</summary>
    Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured
    /// sending number within [sentAfter, sentBefore). The sending-number filter is applied
    /// by the provider, not client-side.
    /// </summary>
    Task<ListMessagesOutcome> ListMessagesAsync(DateTimeOffset sentAfter, DateTimeOffset sentBefore, CancellationToken ct = default);

    /// <summary>Erases a message's text at the provider while keeping the message record.</summary>
    Task<MessagingOutcome> RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);
}
