using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Error);

public record SmsSendResult(bool Success, string? MessageSid, string? Status, string? ErrorCode, string? ErrorMessage);

public record SmsMessageInfo(string Sid, string Status, string? To, string? From,
    DateTimeOffset? DateSent, string? ErrorCode);

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio). Implementations must never
/// log phone numbers or message bodies.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Validates a phone number with the provider and returns the provider's canonical form of it.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message immediately from the application's configured sending number.
    /// </summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at a later time (provider-held scheduling).
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a provider-scheduled message that has not yet been sent.
    /// Returns false when the provider reports the message is no longer cancellable.
    /// </summary>
    Task<bool> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the provider's current state for a message. Null when the provider has no such message.
    /// </summary>
    Task<SmsMessageInfo?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently redacts the message body at the provider while keeping the message record.
    /// </summary>
    Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the given (UTC) range. The sender filter is applied by the provider.
    /// </summary>
    Task<IReadOnlyList<SmsMessageInfo>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
