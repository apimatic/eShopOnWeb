using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary to the SMS provider. Implementations translate provider
/// failures into <see cref="Exceptions.SmsProviderException"/>; they never leak SDK types.
/// Destination numbers must never be written to logs by implementations.
/// </summary>
public interface ISmsService
{
    /// <summary>Asks the provider whether the number is a usable destination and returns its canonical form.</summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message now, from the application's configured sending number.</summary>
    Task<SmsSendResult> SendSmsAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider to be sent at a later time (provider-side scheduling).</summary>
    Task<SmsSendResult> ScheduleSmsAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a provider-scheduled message that has not yet gone out.</summary>
    Task CancelScheduledSmsAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current record of a message.</summary>
    Task<SmsMessageStatusResult> GetMessageStatusAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Erases the message's body text at the provider; the record and its status survive.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured
    /// sending number within (fromUtc, toUtc), paging through the whole range.
    /// </summary>
    Task<ProviderSmsListResult> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
