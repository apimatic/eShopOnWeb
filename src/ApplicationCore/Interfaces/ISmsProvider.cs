using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Outcome of asking the provider to accept a message (immediate send, scheduled send, or cancel).
/// <paramref name="Accepted"/> reports whether the provider took the message; when it did not,
/// the error fields explain why so the caller can record it without failing the underlying operation.
/// </summary>
public record SmsSendResult(
    bool Accepted,
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// The provider's own record of a message: its identifier and current delivery outcome, plus the
/// fields reconciliation lines up against what eShop believes it sent.
/// </summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    string? Body,
    DateTimeOffset? DateSent);

/// <summary>
/// A hand-written client for the provider's messaging API, built to the OpenAPI contract in
/// <c>api-specs/twilio/twilio_api_v2010</c>. Every messaging call goes through the messaging base
/// address (the configurable <c>Twilio:BaseUrl</c> override, or the provider default).
/// </summary>
public interface ISmsProvider
{
    /// <summary>This application's own configured sending number (<c>Twilio:FromNumber</c>).</summary>
    string SendingNumber { get; }

    /// <summary>Sends a message now, from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>Queues a message with the provider to be sent at a future time.</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>Reads the provider's current record for a message.</summary>
    Task<ProviderMessage?> FetchAsync(string sid, CancellationToken cancellationToken);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken);

    /// <summary>Redacts a message's body at the provider so its text is no longer retrievable there.</summary>
    Task<bool> RedactBodyAsync(string sid, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the provider's own record of messages sent from the given number within a date range,
    /// asking the provider to filter by that sending number rather than filtering a wider answer here.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(string fromE164, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
