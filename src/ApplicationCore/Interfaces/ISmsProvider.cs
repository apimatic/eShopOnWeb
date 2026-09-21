using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the SMS messaging provider (implemented for Twilio in Infrastructure). Kept free of any
/// provider SDK type so the domain never depends on the provider. All state the provider owns — the
/// message identifier and its delivery outcome — is surfaced through the plain DTOs below.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Ask the provider whether a number is a usable destination and, if so, its canonical E.164 form.</summary>
    Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken);

    /// <summary>Send a message now. A returned <see cref="SmsDispatchResult.ProviderMessageSid"/> means the
    /// provider accepted it; <see cref="SmsDispatchResult.SendError"/> means it could not be handed over.</summary>
    Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/> (a few days out).</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>Cancel a scheduled message with the provider before it goes out.</summary>
    Task<SmsDispatchResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>Read the provider's current delivery outcome for a message.</summary>
    Task<SmsDeliveryStatus> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>Dispose of the message's content at the provider so its text is no longer retrievable there.</summary>
    Task DeleteContentAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>List the provider's own record of messages sent from this application's configured sending
    /// number within the given window (inclusive range), across the whole range.</summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Outcome of a number validation.</summary>
public record PhoneNumberValidation(bool IsValid, string? CanonicalE164, string? Reason);

/// <summary>Outcome of a send/schedule/cancel attempt.</summary>
public record SmsDispatchResult(string? ProviderMessageSid, string? Status, int? ErrorCode, string? ErrorMessage, string? SendError)
{
    public bool Accepted => ProviderMessageSid is not null && SendError is null;

    public static SmsDispatchResult Ok(string sid, string? status) => new(sid, status, null, null, null);
    public static SmsDispatchResult Failed(string sendError) => new(null, null, null, null, sendError);
}

/// <summary>The provider's current delivery outcome for a message.</summary>
public record SmsDeliveryStatus(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One entry from the provider's own list of messages (for reconciliation).</summary>
public record ProviderMessageRecord(string Sid, string? Status, string? To, string? From, DateTimeOffset? DateSent, int? ErrorCode);
