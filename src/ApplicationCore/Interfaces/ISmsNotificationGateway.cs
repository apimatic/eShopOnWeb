using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of the SMS provider. Every Twilio-specific detail lives behind this
/// abstraction (implemented in Infrastructure); the domain and orchestration layers depend only
/// on this interface. Implementations translate provider/transport failures into
/// <see cref="Exceptions.SmsGatewayException"/> so callers have a single failure type to handle.
/// </summary>
public interface ISmsNotificationGateway
{
    /// <summary>
    /// The configured sending number (Twilio:FromNumber) that immediate notifications go out from.
    /// Reconciliation lines up the provider's record of traffic from this number.
    /// </summary>
    string SendingNumber { get; }

    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical E.164 form.
    /// A number the provider rejects (invalid, or a hard 4xx lookup failure) comes back as not usable;
    /// this method throws only when the provider itself is unavailable.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Send an SMS now. Returns the provider's identifier and initial status once accepted.</summary>
    Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>Queue an SMS with the provider to be sent at <paramref name="sendAt"/> (a few days out).</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancel a still-scheduled message at the provider so it never goes out.</summary>
    Task CancelScheduledMessageAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>Re-read the provider's current delivery outcome for a message.</summary>
    Task<SmsDeliveryState> FetchDeliveryStateAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Dispose of a message's content at the provider (the body is cleared) while the record that a
    /// message was sent, and what became of it, survives.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Ask the provider for its own record of every message sent from <see cref="SendingNumber"/>
    /// between the two instants (inclusive), covering the whole range via pagination.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Outcome of a phone-number validation at registration time.</summary>
public sealed record PhoneValidationResult(bool IsUsable, string? CanonicalPhoneNumber, string? Reason)
{
    public static PhoneValidationResult Usable(string canonicalE164) => new(true, canonicalE164, null);
    public static PhoneValidationResult NotUsable(string reason) => new(false, null, reason);
}

/// <summary>Result of the provider accepting a send/schedule request.</summary>
public sealed record SmsDispatchResult(string ProviderMessageSid, string DeliveryStatus, string FromNumber);

/// <summary>The provider's current view of a message's delivery.</summary>
public sealed record SmsDeliveryState(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider records it (for reconciliation).</summary>
public sealed record ProviderMessageRecord(
    string Sid,
    string? Status,
    string? From,
    string? To,
    DateTimeOffset? DateSent);
