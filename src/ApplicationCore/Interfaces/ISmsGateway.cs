using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's message API, as the application needs it: send a message now, schedule one for
/// later, read a message's current delivery state, call off a not-yet-sent message, dispose of a message's
/// content, and list the provider's own record of messages sent from the configured sending number.
/// </summary>
public interface ISmsGateway
{
    /// <summary>Send a message immediately.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ask the provider to send a message at <paramref name="sendAt"/>. The provider holds and later sends it;
    /// the application does not run a timer of its own.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current delivery state for a message.</summary>
    Task<SmsMessageState> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Call off a message the provider has not yet sent (a scheduled message).</summary>
    Task<SmsMessageState> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider so its text can no longer be retrieved, while the fact
    /// that the message was sent and what became of it survive.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending number over a
    /// date range. The provider is asked for that number's messages directly, not filtered afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The result of handing a message to the provider.</summary>
/// <param name="ProviderMessageSid">The provider's identifier for the message.</param>
/// <param name="Status">The message's initial status at the provider.</param>
public record SmsSendResult(string ProviderMessageSid, string? Status);

/// <summary>The provider-owned delivery state of a message.</summary>
public record SmsMessageState(string ProviderMessageSid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One row of the provider's own record of a message, for reconciliation.</summary>
public record ProviderMessageRecord(
    string Sid,
    string? Status,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
