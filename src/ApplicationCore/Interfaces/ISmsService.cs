using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public record SmsSendResult(string MessageSid, string Status);

public record SmsMessageState(string MessageSid, string Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateSent);

public record SmsMessageRecord(string MessageSid, string From, string To, string Status,
    DateTimeOffset? DateSent, DateTimeOffset? DateCreated, int? ErrorCode, string? ErrorMessage);

/// <summary>
/// Abstraction over the SMS provider (Twilio). Implementations must never log
/// destination phone numbers or credentials.
/// </summary>
public interface ISmsService
{
    /// <summary>Validates a number with the provider and returns its canonical (E.164) form.</summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider for delivery at a future time.</summary>
    Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Gets the provider's current state for a previously sent message.</summary>
    Task<SmsMessageState> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-scheduled message that has not yet gone out.</summary>
    Task<SmsMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider so it is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured
    /// sending number whose sent date falls within [from, to]. The From filter is applied
    /// by the provider, not client-side.
    /// </summary>
    Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
