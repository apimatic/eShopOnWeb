using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port through which the application talks to the SMS provider. It is deliberately free of
/// any provider SDK types so the application layer never depends on the messaging vendor.
///
/// Contract of the send operations: <see cref="SendAsync"/> and <see cref="ScheduleAsync"/> never
/// throw — a message that cannot be sent is returned as a non-accepted result so it can never fail
/// the underlying order operation. <see cref="ValidateNumberAsync"/>, <see cref="RedactContentAsync"/>
/// and <see cref="ListSentAsync"/> surface a provider/transport failure as <see cref="SmsGatewayException"/>
/// because those front caller-facing operations that must not silently succeed.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// This application's own configured sending number (<c>Twilio:FromNumber</c>). Non-secret; used to
    /// scope the reconciliation report to this application's traffic. Never a credential.
    /// </summary>
    string SendingNumber { get; }

    /// <summary>
    /// Ask the provider whether the number is a usable destination and, if so, its canonical form.
    /// Throws <see cref="SmsGatewayException"/> if the provider could not be reached.
    /// </summary>
    Task<SmsValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Send a message now. Never throws.</summary>
    Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>. Never throws.</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Read the provider's current delivery outcome for a message. Never throws (returns not-found on failure).</summary>
    Task<SmsStatusResult> FetchStatusAsync(string messageSid, CancellationToken ct);

    /// <summary>Cancel a not-yet-sent (scheduled) message so it never reaches the shopper. Never throws.</summary>
    Task<bool> CancelScheduledAsync(string messageSid, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider (redaction): the text is no longer retrievable,
    /// while the record and its outcome survive. Throws <see cref="SmsGatewayException"/> on failure.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken ct);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending
    /// number, over a date range. Throws <see cref="SmsGatewayException"/> on failure.
    /// </summary>
    Task<SmsListResult> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Result of asking the provider to validate a candidate destination number.</summary>
public sealed record SmsValidationResult(bool IsUsable, string? CanonicalE164, string? Reason);

/// <summary>
/// Result of asking the provider to send/queue a message. <see cref="Accepted"/> reflects only that
/// the provider took the request; the delivery outcome (<see cref="Status"/>) evolves afterwards.
/// </summary>
public sealed record SmsDispatchResult(
    bool Accepted,
    string? MessageSid,
    string? Status,
    int? ErrorCode,
    string? ErrorDescription,
    string? FailureReason);

/// <summary>Result of reading a message's current delivery outcome from the provider.</summary>
public sealed record SmsStatusResult(bool Found, string? Status, int? ErrorCode, string? ErrorDescription);

/// <summary>One message as the provider records it, for reconciliation.</summary>
public sealed record SmsProviderMessage(
    string Sid,
    string? Status,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode);

/// <summary>Result of listing the provider's messages; <see cref="Truncated"/> flags a capped sweep.</summary>
public sealed record SmsListResult(IReadOnlyList<SmsProviderMessage> Messages, bool Truncated);

/// <summary>Raised when the SMS provider could not be reached or answered with an error on a
/// caller-facing operation (validation, redaction, reconciliation).</summary>
public sealed class SmsGatewayException : Exception
{
    public int? StatusCode { get; }

    public SmsGatewayException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
