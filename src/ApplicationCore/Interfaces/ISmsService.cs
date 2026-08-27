using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio).
/// Implementations must never throw for provider-side rejections of a message;
/// those are returned as results. Implementations must never log phone numbers.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Asks the provider whether the given number is a usable destination and
    /// returns the provider's canonical form of the number.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message immediately, or queues it with the provider for later
    /// delivery when <paramref name="sendAt"/> is supplied.
    /// </summary>
    Task<SmsSendResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a provider-scheduled message that has not yet gone out.
    /// </summary>
    Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the body of a message at the provider while keeping the rest of
    /// the provider's record (and its delivery outcome) intact.
    /// </summary>
    Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider for the current delivery outcome of a message.
    /// Returns null when the message is unknown to the provider.
    /// </summary>
    Task<SmsMessageRecord?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider for its own record of messages sent from this
    /// application's configured sending number within the given range
    /// (provider-side filtering), following pagination to cover the whole range.
    /// </summary>
    Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
