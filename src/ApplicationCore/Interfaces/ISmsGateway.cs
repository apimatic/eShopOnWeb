using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio) covering every capability the order-notification
/// integration needs: validating a destination, sending and scheduling messages, cancelling a
/// scheduled message, reading a message's current state, redacting a message body at the provider,
/// and listing the provider's own record of messages for reconciliation.
/// </summary>
public interface ISmsGateway
{
    /// <summary>This application's own configured sending number (Twilio:FromNumber), in E.164.</summary>
    string ConfiguredFromNumber { get; }

    /// <summary>
    /// Validates a destination with the provider and returns its canonical E.164 form. A number the
    /// provider does not consider a usable destination comes back with <see cref="PhoneLookupResult.IsValid"/> false.
    /// </summary>
    Task<PhoneLookupResult> LookupNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately. Never throws for a provider send failure; the outcome is in the result.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a message with the provider for future delivery. The provider holds it until <paramref name="sendAt"/>;
    /// it is not held in this application.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a message that is still scheduled at the provider so it never goes out.
    /// Returns the provider's status after the attempt.
    /// </summary>
    Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads a single message's current state from the provider.</summary>
    Task<ProviderMessage?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's text at the provider so it is no longer retrievable there, while the message
    /// record and its delivery outcome survive.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending number
    /// within a date range, following pagination to cover the whole range. The sender filter is applied by
    /// the provider, not after the fact, so traffic from other numbers on the account is excluded.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of validating a phone number with the provider.</summary>
public record PhoneLookupResult(bool IsValid, string? CanonicalE164, string? Reason);

/// <summary>Provider response to a send / schedule / cancel attempt.</summary>
public record SmsSendResult(bool Accepted, string? ProviderMessageSid, string Status, string? ErrorCode, string? ErrorMessage);

/// <summary>The provider's view of a message, used for status refresh and reconciliation.</summary>
public record ProviderMessage(
    string Sid,
    string Status,
    string? To,
    string? From,
    string? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
