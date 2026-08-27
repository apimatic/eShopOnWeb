using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio). Implementations must never
/// throw for a well-formed request that the provider rejects at send time — that outcome
/// is reported through <see cref="SmsSendResult"/>. Transport/auth failures may throw.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Validates a phone number with the provider and returns the provider's canonical
    /// (E.164) form. Invalid numbers are reported, not thrown.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately.</summary>
    Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current state for one message.</summary>
    Task<SmsMessageState?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-scheduled message that has not yet been sent.</summary>
    Task<bool> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so the text is no longer retrievable there.</summary>
    Task<bool> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within a date range (provider-side filter), following all pages.
    /// </summary>
    Task<IReadOnlyList<SmsMessageState>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
