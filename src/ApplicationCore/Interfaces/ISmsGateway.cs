using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) as the application sees it. Everything below is a direct
/// projection of the provider's OpenAPI contract; the implementation lives in the Infrastructure
/// layer and is the only place that speaks HTTP to the provider.
/// </summary>
public interface ISmsGateway
{
    /// <summary>This application's own configured sending number (E.164), used to scope reconciliation.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Validates and canonicalises a phone number (Twilio Lookup v2). Returns the provider's own
    /// canonical E.164 form and whether the provider considers the number a usable destination.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. Returns the provider's identifier and initial status.</summary>
    Task<SmsSubmissionResult> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/> (a fixed-time scheduled message).</summary>
    Task<SmsSubmissionResult> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record for a message (its delivery outcome and any error code).</summary>
    Task<SmsDeliveryState> GetMessageStateAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is still scheduled and has not yet been sent.</summary>
    Task CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts a message's body at the provider so its text can no longer be retrieved there.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number within the given range, across the whole range. The sender filter is applied by the
    /// provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a Lookup: whether the number is usable and its canonical E.164 form.</summary>
public sealed record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>The provider's response to creating a message.</summary>
public sealed record SmsSubmissionResult(string MessageSid, string Status);

/// <summary>A reading of the provider's current state for a message.</summary>
public sealed record SmsDeliveryState(string Status, int? ErrorCode);

/// <summary>A message as it appears in the provider's own records, used for reconciliation.</summary>
public sealed record ProviderMessageRecord(string Sid, string? Status, DateTimeOffset? DateSent, string? To, string? From);
