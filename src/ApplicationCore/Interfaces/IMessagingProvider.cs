using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the SMS provider (Twilio). Everything the application needs to know about a message
/// after it leaves — its identifier and delivery outcome — is obtained by asking the provider,
/// because the provider cannot call back into this application. Implementations must never write
/// a destination number or the auth secret to logs.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>This application's own configured sending number (Twilio:FromNumber), in E.164.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Validate a caller-supplied number and return the provider's canonical E.164 form.
    /// A number the provider does not consider a usable destination comes back with IsValid = false.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now, from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue a message with the provider for future delivery. The provider holds it and sends it at
    /// <paramref name="sendAt"/> — this application does not keep a timer of its own.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider has scheduled but not yet sent.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's body at the provider while leaving the message record intact.</summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Read the current delivery outcome of a single message from the provider.</summary>
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending
    /// number within the range. The provider is asked for that number's messages directly.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of validating/canonicalising a phone number.</summary>
public record PhoneNumberLookupResult(bool IsValid, string? E164);

/// <summary>
/// A message as the provider reports it. <see cref="To"/> is included for the provider's own listing
/// but must never be surfaced to callers or logs by the application.
/// </summary>
public record ProviderMessage(
    string Sid,
    string Status,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
