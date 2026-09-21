using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The shop's view of the SMS provider. Implementations wrap the Twilio SDK; every method translates
/// provider/transport failures into <see cref="Exceptions.MessagingProviderException"/> so callers have a
/// single failure type to handle. Phone numbers and message bodies are never logged by implementations.
/// </summary>
public interface ITwilioMessagingService
{
    /// <summary>
    /// Ask the provider whether <paramref name="rawNumber"/> is a usable destination and, if so, its
    /// canonical E.164 form. Used to reject bad numbers at registration rather than at send time.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Send an SMS now, from the app's configured sending number.</summary>
    Task<MessageSendResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>
    /// Queue an SMS with the provider to be sent at <paramref name="sendAt"/> (via the messaging service),
    /// so the delivery survey is held by the provider — not by a timer in this application.
    /// </summary>
    Task<MessageSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Read the provider's current delivery state for a message the app previously created.</summary>
    Task<MessageDeliveryState> FetchStatusAsync(string sid, CancellationToken ct = default);

    /// <summary>Cancel a not-yet-sent scheduled message so it never reaches the shopper.</summary>
    Task<MessageDeliveryState> CancelScheduledAsync(string sid, CancellationToken ct = default);

    /// <summary>Redact the message body at the provider so its text is no longer retrievable there.</summary>
    Task RedactBodyAsync(string sid, CancellationToken ct = default);

    /// <summary>
    /// List the provider's own record of messages sent from this app's configured sending number within
    /// the date range, covering the whole range (paginated). Asks the provider for that number's messages
    /// rather than filtering a wider answer after the fact.
    /// </summary>
    Task<ProviderMessageListing> ListSentByAppAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
