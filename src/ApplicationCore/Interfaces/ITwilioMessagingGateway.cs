using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single seam over the messaging provider (Twilio). Every provider interaction the
/// integration performs goes through here. Implementations translate provider failures into
/// <see cref="Exceptions.NotificationProviderException"/> and never leak the destination number
/// or the auth token into logs or exception messages.
/// </summary>
public interface ITwilioMessagingGateway
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and return its canonical E.164 form.
    /// Validity is reported in the result, not by throwing; a provider outage throws.
    /// </summary>
    Task<PhoneNumberValidation> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Send an SMS now. Returns the accepted message's SID and initial status.</summary>
    Task<ProviderSendResult> SendMessageAsync(string toNumber, string body, CancellationToken ct = default);

    /// <summary>Schedule an SMS to be sent by the provider at <paramref name="sendAt"/>.</summary>
    Task<ProviderSendResult> ScheduleMessageAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancel a message the provider has scheduled but not yet sent.</summary>
    Task<ProviderMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Read a message's current delivery outcome from the provider.</summary>
    Task<ProviderMessageState> FetchMessageStateAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Dispose of a message's body at the provider so its text is no longer retrievable, while the record survives.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// List the provider's own record of messages sent from the configured sending number within a
    /// date range, covering the whole range. Filtering by sender is done in the provider request.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Outcome of validating a phone number.</summary>
public record PhoneNumberValidation(bool IsValid, string? CanonicalE164, IReadOnlyList<string> Reasons);

/// <summary>Outcome of an accepted (or attempted) send.</summary>
public record ProviderSendResult(string? Sid, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A message's current delivery state at the provider.</summary>
public record ProviderMessageState(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider reports it during reconciliation. Deliberately carries no destination number.</summary>
public record ProviderMessageRecord(string Sid, string Status, string? DateSent, int? ErrorCode);
