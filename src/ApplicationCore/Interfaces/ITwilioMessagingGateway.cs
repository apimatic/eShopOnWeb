using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's only door to the Twilio messaging API — send, schedule, cancel, fetch status,
/// list (for reconciliation) and redact. Every method translates provider/transport failures into
/// <see cref="ProviderGatewayException"/>. Implementations must never let a phone number or message
/// body reach a log.
/// </summary>
public interface ITwilioMessagingGateway
{
    /// <summary>Send an SMS immediately from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>Queue an SMS with the provider to be sent at <paramref name="sendAt"/> (scheduled message).</summary>
    Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Fetch the provider's current view of a message (delivery status/outcome).</summary>
    Task<ProviderMessage> FetchAsync(string sid, CancellationToken ct);

    /// <summary>Cancel a not-yet-sent scheduled message so it never goes out.</summary>
    Task CancelScheduledAsync(string sid, CancellationToken ct);

    /// <summary>Dispose of a message's text at the provider (redaction), keeping the message record.</summary>
    Task RedactContentAsync(string sid, CancellationToken ct);

    /// <summary>
    /// Ask the provider for its own record of messages sent from this application's configured
    /// sending number within a date range, walking all pages up to a cap.
    /// </summary>
    Task<ProviderMessageListing> ListForFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Validates a caller-supplied phone number against the provider and returns its canonical form.</summary>
public interface IPhoneNumberValidator
{
    Task<PhoneValidationResult> ValidateAsync(string rawNumber, CancellationToken ct);
}
