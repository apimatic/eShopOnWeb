using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over the SMS provider. The concrete implementation talks to the
/// provider's SDK; the rest of the application depends only on this. Every method either succeeds or
/// throws <see cref="Exceptions.SmsProviderException"/> — a single failure type — so callers never have
/// to reason about the underlying SDK's exception zoo. Implementations must never write a destination
/// number to any log.
/// </summary>
public interface ISmsProvider
{
    /// <summary>This application's own configured sending number (Twilio:FromNumber).</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Asks the provider whether <paramref name="rawNumber"/> is a usable destination and, when it is,
    /// returns the provider's own canonical (E.164) form of it.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Sends an SMS now. Returns the provider's identifier and initial delivery status.</summary>
    Task<SentSmsMessage> SendAsync(string toNumber, string body, CancellationToken ct = default);

    /// <summary>Queues an SMS with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SentSmsMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Calls off a not-yet-sent scheduled message so it never goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current delivery outcome for a message. Returns null if unknown.</summary>
    Task<string?> FetchStatusAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there,
    /// while the send-record and its delivery outcome survive.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number within a date range. The From-number and date filtering are applied by the provider,
    /// not by scanning a wider answer here.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Result of a validity/canonicalization lookup.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>What the provider returned when a message was created or scheduled.</summary>
public record SentSmsMessage(string Sid, string? Status);

/// <summary>The provider's own record of one message, as returned by a reconciliation list.</summary>
public record ProviderMessageRecord(string? Sid, string? To, string? From, string? Status, DateTimeOffset? DateSent, string? Body);
