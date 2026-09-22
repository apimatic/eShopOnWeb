using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin, domain-facing wrapper over the Twilio messaging API. It never leaks SDK types and
/// translates every provider/transport failure into <see cref="ProviderMessagingException"/> at this
/// boundary, so callers deal with a single failure type. All calls are bounded by a total timeout.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>The application's configured sending number (<c>Twilio:FromNumber</c>).</summary>
    string FromNumber { get; }

    /// <summary>Validates and canonicalizes a number via the provider's Lookup. Throws on transport/provider error.</summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Sends an immediate message from the configured <c>FromNumber</c>.</summary>
    Task<SentMessage> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>Schedules a message for a future time via the configured Messaging Service.</summary>
    Task<SentMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message.</summary>
    Task<SentMessage> CancelScheduledAsync(string providerSid, CancellationToken ct = default);

    /// <summary>Fetches the current provider state for a message.</summary>
    Task<SentMessage> FetchAsync(string providerSid, CancellationToken ct = default);

    /// <summary>Redacts (disposes of) a message's body at the provider; the record and its status survive.</summary>
    Task RedactAsync(string providerSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's messages sent from <c>FromNumber</c> whose provider send-time falls in
    /// [<paramref name="from"/>, <paramref name="to"/>]. The provider filters by sender; pages are
    /// walked up to <paramref name="maxPages"/>. <see cref="ProviderMessagePage.Truncated"/> reports
    /// if the cap bounded coverage.
    /// </summary>
    Task<ProviderMessagePage> ListSentFromNumberAsync(DateTimeOffset from, DateTimeOffset to, int maxPages, CancellationToken ct = default);
}

/// <summary>Result of a phone-number Lookup.</summary>
public record PhoneValidationResult(bool IsValid, string? E164Number, string? CountryCode);

/// <summary>The state the provider owns for a single message.</summary>
public record SentMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>A provider message as seen by the reconciliation listing.</summary>
public record ProviderMessage(string? Sid, string? Status, string? To, string? From, DateTimeOffset? DateSent);

/// <summary>A (possibly truncated) page-walk of provider messages.</summary>
public record ProviderMessagePage(IReadOnlyList<ProviderMessage> Messages, bool Truncated);
