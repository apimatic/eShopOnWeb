using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of the SMS provider. Implemented in Infrastructure over the Twilio SDK,
/// so nothing in ApplicationCore depends on the provider or its types.
///
/// Contract for the notification flow:
/// <list type="bullet">
/// <item><see cref="ValidateNumberAsync"/> and <see cref="RedactContentAsync"/> report failure by
/// throwing — the caller needs to know when validation or content disposal did not happen.</item>
/// <item><see cref="SendAsync"/>, <see cref="ScheduleAsync"/> and <see cref="CancelScheduledAsync"/>
/// never throw for provider/transport errors — they return an outcome — because a message that
/// cannot be sent (or a follow-up that cannot be cancelled) must never fail the underlying order
/// operation.</item>
/// </list>
/// </summary>
public interface ISmsGateway
{
    /// <summary>The configured sending number (Twilio:FromNumber) this integration sends and reconciles from.</summary>
    string ConfiguredFromNumber { get; }

    /// <summary>
    /// Asks the provider whether the number is a usable destination and returns its canonical E.164
    /// form. Throws <see cref="Exceptions.SmsGatewayException"/> if the provider cannot be reached.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message now, from the configured sending number. Never throws on provider/transport error.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>. Never throws on provider/transport error.</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a message the provider has queued but not yet sent. Never throws on provider/transport error.</summary>
    Task<SmsCancelResult> CancelScheduledAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Reads a single message's current delivery outcome from the provider. Returns null if it cannot be read.</summary>
    Task<SmsDeliveryOutcome?> FetchStatusAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number within the
    /// date range, asking the provider to filter by that number rather than filtering a wider answer here.
    /// Throws <see cref="Exceptions.SmsGatewayException"/> if the provider cannot be reached.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's text at the provider so it is no longer retrievable there, while the
    /// record that the message was sent, and its outcome, survive. Throws
    /// <see cref="Exceptions.SmsGatewayException"/> if the provider cannot be reached or refuses.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken ct = default);
}

/// <summary>Result of a provider phone-number lookup.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalE164);

/// <summary>Outcome of an immediate or scheduled send. <see cref="Status"/> is the provider's wire status when accepted.</summary>
public record SmsSendResult(bool Accepted, string? MessageSid, string Status, int? ErrorCode, string? FailureReason);

/// <summary>Outcome of cancelling a scheduled message.</summary>
public record SmsCancelResult(bool Canceled, string? FailureReason);

/// <summary>A single message's current delivery outcome as the provider reports it.</summary>
public record SmsDeliveryOutcome(string Status, int? ErrorCode);

/// <summary>The provider's own record of a message, used for reconciliation.</summary>
public record ProviderMessageRecord(string Sid, string Status, string? From, string? To, int? ErrorCode, DateTimeOffset? DateSent);
