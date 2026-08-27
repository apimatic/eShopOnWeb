using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shop's text-messaging provider. Implementations must never log destination numbers
/// or credentials.
/// </summary>
public interface ITextMessagingService
{
    /// <summary>
    /// Validates a number with the provider and returns the provider's canonical form.
    /// Throws InvalidPhoneNumberException when the provider does not consider the number
    /// a usable destination.
    /// </summary>
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the shop's configured sending number.</summary>
    Task<TextMessageResult> SendMessageAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider to be sent at a future time.</summary>
    Task<TextMessageResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a provider-queued message that has not yet gone out.</summary>
    Task<TextMessageResult> CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current record of a single message.</summary>
    Task<TextMessageResult> GetMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Erases the message's text at the provider; the message record itself survives.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the shop's configured sending
    /// number whose sent date falls inside [from, to], covering the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderTextMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
