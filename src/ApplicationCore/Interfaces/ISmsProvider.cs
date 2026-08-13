using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The one seam through which the application talks to the SMS provider's messaging API. Everything
/// provider-specific (host, credentials, sender selection, wire format) lives behind this interface;
/// callers deal only in E.164 destinations, message bodies, and provider message identifiers.
/// </summary>
public interface ISmsProvider
{
    /// <summary>The application's configured sending number — the only sender reconciliation counts.</summary>
    string ConfiguredSenderNumber { get; }

    /// <summary>
    /// Create and enqueue an outbound message to <paramref name="toPhoneNumber"/> from the configured
    /// sending number. Returns the provider's identifier and initial status. A provider-side rejection
    /// (an invalid or refused request) is returned as a result with a null SID and an error code — it is
    /// an outcome, not an exception.
    /// </summary>
    Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> (a fixed schedule). The
    /// provider holds it until then; it is not retained in this application to be sent by a timer of its own.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current authoritative record for one message by its identifier.</summary>
    Task<SmsMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a message that has not yet been sent (a scheduled follow-up), so it never reaches the shopper.
    /// Returns the message's state after cancellation.
    /// </summary>
    Task<SmsMessageState> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of the text of a message at the provider so it can no longer be retrieved there, while the
    /// record that a message was sent and what became of it survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages the configured sending number sent within the range, asked of
    /// the provider by sender so that only this application's traffic is counted.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageSummary>> ListOutboundFromConfiguredSenderAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of creating (sending or scheduling) a message at the provider.</summary>
public record SmsSendResult(string? ProviderMessageSid, string Status, int? ErrorCode, string? ErrorMessage)
{
    public bool Created => !string.IsNullOrEmpty(ProviderMessageSid);
}

/// <summary>The provider's current record for one message.</summary>
public record SmsMessageState(string ProviderMessageSid, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A single message as it appears in the provider's own list, used for reconciliation.</summary>
public record ProviderMessageSummary(
    string ProviderMessageSid, string Status, string? To, string? From, DateTimeOffset? DateSent, int? ErrorCode);
