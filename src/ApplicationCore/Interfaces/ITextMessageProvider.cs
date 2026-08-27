using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's view of a single message: its identifier and
/// current delivery outcome, per the provider's published API contract.
/// </summary>
public sealed record ProviderMessage(
    string MessageSid,
    string Status,
    string? To,
    string? From,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable
/// destination and the provider's canonical form of it.
/// </summary>
public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, string? ValidationError);

/// <summary>
/// Abstraction over the SMS provider (Twilio). Implementations are built
/// against the provider's OpenAPI specification, not a pre-built SDK.
/// </summary>
public interface ITextMessageProvider
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message.</summary>
    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider has not sent yet.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's text at the provider so it is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's
    /// configured sending number within [from, to].
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
