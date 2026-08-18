using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider seam. Everything eShop knows about the SMS provider goes through here, so the
/// rest of the application never depends on the provider SDK. Every method throws
/// <see cref="Exceptions.SmsGatewayException"/> when the provider rejects a request or cannot be reached;
/// callers decide whether that should surface or be swallowed (a failed notification must never fail the
/// underlying order operation).
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber). Never a shopper's number.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Asks the provider whether the number is a usable destination and returns the provider's canonical
    /// (E.164) form of it. Used to reject an unusable number at registration rather than at send time.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Sends an SMS now, from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>
    /// Queues an SMS with the provider to be sent at <paramref name="sendAt"/> (the provider holds it, not
    /// this application). Returns the provider identifier so it can be called off before it goes out.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Calls off a still-scheduled message at the provider before it is sent.</summary>
    Task CancelScheduledAsync(string providerSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current delivery outcome for a message.</summary>
    Task<SmsDeliveryState> FetchDeliveryStateAsync(string providerSid, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there, while
    /// the record that a message existed and its delivery outcome survive.
    /// </summary>
    Task RedactContentAsync(string providerSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending number
    /// within the date range. Asks the provider for that number's messages (it does not filter a wider
    /// answer after the fact), and covers the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Outcome of validating/canonicalizing a phone number with the provider.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalE164);

/// <summary>What the provider returned when a message was created (sent or scheduled).</summary>
public record SmsSendResult(string? ProviderSid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current delivery outcome for a message.</summary>
public record SmsDeliveryState(string? Status, int? ErrorCode, string? ErrorMessage, string? DateSent);

/// <summary>The provider's own record of one message, as seen during reconciliation.</summary>
public record ProviderMessageRecord(string Sid, string? Status, DateTimeOffset? DateSent, int? ErrorCode);
