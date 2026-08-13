using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). The application core depends only on this
/// contract; the concrete HTTP integration lives in Infrastructure. Implementations must never
/// write phone numbers or provider secrets to logs.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, return its canonical
    /// E.164 form. Used to reject unusable numbers at registration rather than at send time.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now. Returns the provider's identifier and initial status.</summary>
    Task<SentMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at a fixed future time.</summary>
    Task<SentMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current delivery status for a message.</summary>
    Task<string> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact a message's body at the provider so its text is no longer retrievable there, while the record survives.</summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ask the provider for its own record of the messages this application sent in a date range,
    /// scoped to the application's configured sending number. Used for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of validating a number against the provider.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalPhoneNumber, string? Reason);

/// <summary>The provider's acknowledgement of a message we asked it to send or schedule.</summary>
public record SentMessage(string Sid, string Status, DateTimeOffset? DateSent);

/// <summary>One message as the provider itself records it, used for reconciliation.</summary>
public record ProviderMessage(string Sid, string To, string From, string Status, DateTimeOffset? DateSent);
