using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public record ProviderMessageResult(
    bool Success,
    string? MessageSid,
    string? Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

public record ProviderMessageRecord(
    string MessageSid,
    string? Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

/// <summary>
/// The messaging provider (Twilio). Implementations must never log destination
/// phone numbers or credentials.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Validates a number with the provider and returns the provider's canonical form of it.</summary>
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessageResult> SendMessageAsync(string toCanonicalNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a future time (provider-held scheduling).</summary>
    Task<ProviderMessageResult> ScheduleMessageAsync(string toCanonicalNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message.</summary>
    Task<ProviderMessageResult?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-held scheduled message that has not yet gone out.</summary>
    Task<ProviderMessageResult?> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider while keeping the message record.</summary>
    Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Lists the provider's own record of messages sent from the application's configured
    /// sending number within the range (server-side filtered), following all pages.</summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
