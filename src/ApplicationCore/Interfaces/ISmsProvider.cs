using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider boundary. Implementations talk to the SMS provider;
/// callers never see provider SDK types.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider whether the given number is a usable destination and,
    /// if so, the provider's canonical form of it.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider itself for a later time (provider-side scheduling).</summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a message the provider has not sent yet.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current record of one message.</summary>
    Task<ProviderMessageState> GetMessageStateAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's text at the provider. The message record and its
    /// delivery outcome survive; the body no longer retrievable afterwards.
    /// </summary>
    Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's
    /// configured sending number within the given range (server-side filtered).
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public sealed record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public sealed record SmsSendResult(string ProviderMessageSid, string ProviderStatus);

public sealed record ProviderMessageState(string Status, int? ErrorCode, string? ErrorMessage, string? Body, DateTimeOffset? DateSent);

public sealed record ProviderMessageRecord(
    string MessageSid,
    string? From,
    string? To,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
