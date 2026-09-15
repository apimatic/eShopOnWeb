using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of the SMS provider. The concrete implementation (Infrastructure) owns
/// the provider SDK, the configured sending number and messaging-service SID, and the base-URL
/// override; callers here work only in terms of destinations, bodies and provider message ids.
/// Every method throws <see cref="Exceptions.SmsGatewayException"/> on a provider or transport
/// failure — callers decide whether that failure is fatal (registration) or must be swallowed
/// so the underlying operation still succeeds (order lifecycle messaging).
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber).</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical
    /// (E.164) form. A number the provider does not consider usable comes back
    /// <see cref="PhoneValidationResult.IsValid"/> = false — it does not throw.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawPhoneNumber, CancellationToken ct = default);

    /// <summary>Sends an SMS now, from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, CancellationToken ct = default);

    /// <summary>
    /// Queues an SMS with the provider for future delivery. The provider holds it until
    /// <paramref name="sendAt"/>; the app does not keep a timer of its own.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a message the provider has not yet sent (a queued/scheduled follow-up).</summary>
    Task CancelScheduledAsync(string providerMessageId, CancellationToken ct = default);

    /// <summary>Reads the provider's current delivery outcome for a message.</summary>
    Task<SmsDeliveryState> GetDeliveryStateAsync(string providerMessageId, CancellationToken ct = default);

    /// <summary>
    /// Disposes a message's text at the provider so it is no longer retrievable there, while the
    /// record that the message was sent and what became of it survives.
    /// </summary>
    Task RedactContentAsync(string providerMessageId, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending
    /// number within [<paramref name="from"/>, <paramref name="to"/>], across the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Outcome of a number-usability check.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>What the provider returned when a message was created (immediate or scheduled).</summary>
public record SmsSendResult(string? ProviderMessageId, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current delivery outcome for a single message.</summary>
public record SmsDeliveryState(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider records it (used for reconciliation).</summary>
public record ProviderMessageRecord(
    string ProviderMessageId,
    string Status,
    string? ToPhoneNumber,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent);
