using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider boundary. Implementations talk to the SMS provider (Twilio);
/// all provider exceptions are converted to <see cref="Exceptions.SmsProviderException"/>.
/// Phone numbers must never be logged by implementations.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Validates a number and returns the provider's canonical form of it.</summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider for delivery at a future time.</summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Calls off a provider-queued message that has not gone out yet.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current state of a message.</summary>
    Task<ProviderMessageState> GetMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Disposes of a message's text at the provider; the record and its outcome survive.</summary>
    Task<SmsSendResult> RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured
    /// sending number within [from, to), covering the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageSummary>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
