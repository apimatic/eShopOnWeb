using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin abstraction over the messaging provider's Messages API (send, schedule, cancel, fetch,
/// redact, list). Implementations talk to the provider strictly through its published contract.
///
/// These methods throw only on genuine transport/contract failures (the provider could not be
/// reached or rejected the request). A message that is accepted but later proves undeliverable is
/// a normal outcome reflected in <see cref="SmsMessageState.Status"/>, not an exception.
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's configured sending number (E.164), as used for reconciliation.</summary>
    string SendingNumber { get; }

    /// <summary>Send a message now, from the application's configured sending number.</summary>
    Task<SmsMessageState> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsMessageState> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<SmsMessageState> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current view of a message.</summary>
    Task<SmsMessageState?> GetAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact a message's body text at the provider; the record and its status survive.</summary>
    Task<SmsMessageState> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from the application's configured number
    /// within [<paramref name="from"/>, <paramref name="to"/>]. The from-number filter is applied
    /// by the provider; the whole range is covered by following provider pagination.
    /// </summary>
    Task<IReadOnlyList<SmsMessageState>> ListSentFromAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The provider's view of a single message.</summary>
public record SmsMessageState(
    string Sid,
    string? Status,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);
