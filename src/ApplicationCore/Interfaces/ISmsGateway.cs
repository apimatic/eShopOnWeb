using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-neutral port for the SMS provider. The only abstraction the application layer knows about;
/// the Twilio SDK is reached exclusively through the implementation of this port. Every method either
/// returns a result or throws the implementation's provider exception — callers decide whether a failure
/// is fatal (it never is for a courtesy notification).
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Validate and canonicalise a number with the provider. A number the provider does not consider a
    /// usable destination comes back with <see cref="PhoneNumberLookupResult.IsValid"/> false.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupNumberAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Send a message now, from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> (via the messaging
    /// service, as the provider requires for scheduled messages). It is held by the provider, not here.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Call off a not-yet-sent scheduled message at the provider.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's text at the provider (redaction), leaving the record of the send and its
    /// outcome intact.
    /// </summary>
    Task RedactAsync(string messageSid, CancellationToken ct);

    /// <summary>Read the provider's current record for one message.</summary>
    Task<ProviderMessageStatus> FetchAsync(string messageSid, CancellationToken ct);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending number
    /// within a date range. The provider does the sender/date filtering; the whole range is walked.
    /// </summary>
    Task<ProviderMessageList> ListSentFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}

/// <summary>Outcome of a provider number lookup.</summary>
public sealed record PhoneNumberLookupResult(bool IsValid, string? CanonicalNumber);

/// <summary>Provider acceptance of a created/scheduled message.</summary>
public sealed record SmsSendResult(string MessageSid, string? Status, int? ErrorCode, string? DateSent);

/// <summary>Provider's current status for a message.</summary>
public sealed record ProviderMessageStatus(string? Status, int? ErrorCode, string? DateSent);

/// <summary>One message as the provider reports it, for reconciliation.</summary>
public sealed record ProviderMessage(string Sid, string? Status, string? To, string? From, string? DateSent,
    int? ErrorCode);

/// <summary>A page-walked list of provider messages, flagged if the walk was capped.</summary>
public sealed record ProviderMessageList(IReadOnlyList<ProviderMessage> Messages, bool Truncated);
