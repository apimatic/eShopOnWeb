using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). Implementations must never
/// expose credentials and must treat phone numbers as sensitive (no logging).
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Validates a phone number with the provider and returns its canonical form.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message immediately, or queues it with the provider for
    /// <paramref name="scheduleAt"/> when supplied.
    /// </summary>
    Task<SmsSendResult> SendAsync(string to, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message, or null if unknown.</summary>
    Task<SmsMessageInfo?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-queued (scheduled) message before it goes out.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider so it is no longer retrievable there.</summary>
    Task<bool> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's
    /// configured sending number within a date range (provider-side filter).
    /// </summary>
    Task<IReadOnlyList<SmsMessageInfo>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Error);

public record SmsSendResult(bool Success, string? MessageSid, string? Status, int? ErrorCode, string? ErrorMessage);

public record SmsMessageInfo(
    string MessageSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);
