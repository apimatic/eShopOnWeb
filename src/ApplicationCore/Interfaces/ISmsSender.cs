using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider, from the application's point of view. Every method maps to a live
/// provider messaging-API call. Implementations must not throw on a rejected send — they report the
/// failure in the returned <see cref="SmsSendResult"/> so a message that cannot be sent never fails
/// the operation that triggered it.
/// </summary>
public interface ISmsSender
{
    /// <summary>Send a message now.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAtUtc"/> (a scheduled follow-up).</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record for a message so its delivery outcome can be mirrored.</summary>
    Task<SmsSendResult> GetStatusAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider has not yet sent (e.g. a scheduled follow-up before it goes out).</summary>
    Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's content at the provider so its text is no longer retrievable there.</summary>
    Task RedactContentAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending
    /// number over a date range, for reconciliation. The sending-number filter is applied at the
    /// provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>This application's configured sending number, for display in reconciliation output.</summary>
    string FromNumber { get; }
}

/// <summary>Outcome of a create/schedule/fetch call against the provider for a single message.</summary>
public class SmsSendResult
{
    /// <summary>True when the provider accepted the request and returned a message record.</summary>
    public bool Accepted { get; init; }

    /// <summary>The provider's message identifier (SID), when one was returned.</summary>
    public string? ProviderSid { get; init; }

    /// <summary>The provider status at the time of the call (queued, scheduled, delivered, …).</summary>
    public string? Status { get; init; }

    /// <summary>The provider error code, when the message failed or the request was rejected.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>A short, number-free reason a request was not accepted, for diagnostics.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>A single message as the provider records it, used to reconcile against what eShop believes it sent.</summary>
public class ProviderMessageRecord
{
    public string Sid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public int? ErrorCode { get; init; }
}
