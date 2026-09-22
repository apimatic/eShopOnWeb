using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A provider-agnostic abstraction over the SMS provider (Twilio). The concrete implementation
/// lives in Infrastructure and owns every provider-SDK detail; nothing above this interface sees
/// the SDK. Send/schedule/cancel operations never throw for a provider-side send failure — they
/// return an outcome — so a message that cannot be sent never fails the underlying order
/// operation. Validation, redaction, fetch and reconciliation surface provider faults as
/// <see cref="SmsProviderException"/> because their callers must know they did not happen.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and return its canonical form.
    /// Throws <see cref="SmsProviderException"/> if the provider cannot be reached.
    /// </summary>
    Task<PhoneValidationResult> ValidateAsync(string rawNumber, CancellationToken ct);

    /// <summary>Send an immediate message. Never throws for a send failure — reports it as an outcome.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> (a future
    /// delivery follow-up). Never throws for a send failure — reports it as an outcome.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Cancel a message the provider has scheduled but not yet sent. Never throws.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken ct);

    /// <summary>Read the provider's current record of a message. Returns null when not found; throws on transport failure.</summary>
    Task<ProviderMessage?> FetchAsync(string providerSid, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider (redaction) so its text is no longer
    /// retrievable, while the record and its outcome survive. Throws if it could not be done.
    /// </summary>
    Task RedactContentAsync(string providerSid, CancellationToken ct);

    /// <summary>
    /// List the provider's own record of messages this application sent (from the configured
    /// sending number only) whose provider send time falls in [from, to]. Walks all pages up to a
    /// cap; the result says whether it was truncated. Throws on transport failure.
    /// </summary>
    Task<ProviderMessageListResult> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>
    /// Find the most recent message this application sent to <paramref name="to"/> at or after
    /// <paramref name="sentAfter"/> — used to settle an unknown outcome after a transport failure.
    /// Returns null when none is found; throws on transport failure.
    /// </summary>
    Task<ProviderMessage?> FindRecentAsync(string to, DateTimeOffset sentAfter, CancellationToken ct);
}

/// <summary>The result of asking the provider to validate a destination number.</summary>
public sealed record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

public enum SmsSendOutcome
{
    /// <summary>The provider accepted the message (immediate or scheduled).</summary>
    Accepted = 0,

    /// <summary>The provider explicitly rejected the request — a definite failure.</summary>
    Rejected = 1,

    /// <summary>Transport failed after the request may have been received — outcome unknown.</summary>
    Unknown = 2
}

/// <summary>The outcome of a send / schedule / cancel attempt.</summary>
public sealed record SmsSendResult
{
    public SmsSendOutcome Outcome { get; init; }
    public string? ProviderSid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

/// <summary>A message as the provider records it.</summary>
public sealed record ProviderMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

/// <summary>The provider's messages over a range, plus whether the walk was cut short by the page cap.</summary>
public sealed record ProviderMessageListResult
{
    public IReadOnlyList<ProviderMessage> Messages { get; init; } = new List<ProviderMessage>();
    public bool Truncated { get; init; }
    public int PagesFetched { get; init; }
}

/// <summary>Raised when the SMS provider cannot be reached or returns a fault the caller must know about.</summary>
public sealed class SmsProviderException : Exception
{
    public int? StatusCode { get; }

    public SmsProviderException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
