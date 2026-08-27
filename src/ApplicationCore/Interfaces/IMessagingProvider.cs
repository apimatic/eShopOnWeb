using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, string? Error);

public record ProviderMessage(
    string Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// The SMS provider (Twilio). Implementations must never log phone numbers or credentials.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>Validates a destination number with the provider and returns its canonical form.</summary>
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider for later delivery (provider-held schedule).</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-held scheduled message that has not gone out yet.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message, or null if unknown.</summary>
    Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Permanently disposes of a message's text at the provider, keeping the message record.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the given range (provider-side filtered by sender).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
