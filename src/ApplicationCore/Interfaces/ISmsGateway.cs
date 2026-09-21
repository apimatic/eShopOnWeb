using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over the SMS provider (Twilio). The concrete implementation lives
/// in Infrastructure and is the only place that talks to the provider SDK. Every method bounds its
/// own provider call and translates provider/transport faults into <see cref="SmsGatewayException"/>
/// (or, for the send paths, into a failed <see cref="SmsSendResult"/> so an order operation is never
/// failed by a message that could not be sent).
/// </summary>
public interface ISmsGateway
{
    /// <summary>The configured sending number (Twilio:FromNumber) messages are sent from and
    /// reconciled against.</summary>
    string ConfiguredSendingNumber { get; }

    /// <summary>
    /// Asks the provider whether the number is a usable destination and returns its canonical E.164
    /// form. A number the provider does not consider usable comes back with
    /// <see cref="PhoneNumberValidationResult.IsValid"/> = false. Throws
    /// <see cref="SmsGatewayException"/> only when the provider itself is unreachable/erroring.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Sends a message immediately from the configured sending number. Never throws for a
    /// provider/transport fault — a failure is returned as a <see cref="SmsSendResult"/> with no SID.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>. Never
    /// throws for a provider/transport fault — a failure is returned as a <see cref="SmsSendResult"/>.</summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Re-reads the current delivery outcome for a message from the provider, or null if it
    /// could not be read (the caller keeps the last known value).</summary>
    Task<SmsStatusResult?> GetStatusAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Cancels a not-yet-sent scheduled message so it never reaches the shopper.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Redacts a message's text at the provider so it can no longer be retrieved there,
    /// while the record and its delivery outcome survive.</summary>
    Task DisposeContentAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number within the
    /// range, covering the whole range. Asks the provider to filter by that number rather than
    /// filtering a wider answer afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Outcome of a provider phone-number lookup.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalE164, string? Reason);

/// <summary>Outcome of a create-message call: the provider SID and the status it reported.</summary>
public record SmsSendResult(string? ProviderMessageSid, string? Status, int? ErrorCode, string? ErrorMessage)
{
    public bool Accepted => ProviderMessageSid is not null;
}

/// <summary>A re-read of a message's current delivery outcome.</summary>
public record SmsStatusResult(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A message as the provider records it, used for reconciliation.</summary>
public record ProviderMessageRecord(
    string? Sid, string? Status, string? From, DateTimeOffset? DateSent, int? ErrorCode);
