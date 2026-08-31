using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio). Implementations must never log
/// destination phone numbers or credentials.
/// </summary>
public interface ISmsNotificationClient
{
    /// <summary>
    /// Validates a number with the provider and returns its canonical (E.164) form.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for delivery at a future time.
    /// </summary>
    Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a provider-scheduled message that has not yet been sent.
    /// </summary>
    Task<SmsSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the body of a message at the provider so its text is no longer retrievable there.
    /// </summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageDetails> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the given UTC date-time range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
