using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider the shop uses to reach shoppers. Implementations must throw
/// <see cref="Exceptions.NotificationProviderException"/> for every provider or transport
/// failure so callers have a single failure type to handle.
/// </summary>
public interface INotificationGateway
{
    /// <summary>Validates a caller-typed number and returns the provider's canonical form.</summary>
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Reads the provider's current record of a message (status, error outcome).</summary>
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Cancels a message the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Erases the message body at the provider; the message record itself survives.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured
    /// sending number whose send date falls inside [fromUtc, toUtc]. Covers the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}

public enum PhoneNumberValidity
{
    Valid = 0,
    Invalid = 1,
    /// <summary>The provider could not confirm validity either way (e.g. no Lookup access).</summary>
    Unverifiable = 2
}

public record PhoneNumberValidation(
    PhoneNumberValidity Validity,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

public record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    DateTimeOffset? DateSent);
