using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider seam. Everything the application needs from the SMS provider goes
/// through here; the concrete implementation (which talks to the provider SDK) lives in the
/// host that owns the SDK reference. No provider SDK types cross this boundary.
///
/// Implementations must never write the destination number, message body, or provider
/// credentials to logs.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Ask the provider whether a raw number is a usable destination and, if so, its canonical
    /// E.164 form. Rejection here is a validation outcome, not a failure to reach the provider.
    /// Throws <see cref="Exceptions.SmsGatewayException"/> only if the provider itself is unreachable/errors.
    /// </summary>
    Task<PhoneValidationResult> ValidateDestinationAsync(string rawNumber, CancellationToken ct);

    /// <summary>
    /// Send a message now. A non-throwing return means the provider accepted the message; the
    /// returned status/error fields carry the outcome (which may still be an undeliverable one).
    /// Throws <see cref="Exceptions.SmsGatewayException"/> only on a real send failure (provider unreachable / non-2xx).
    /// </summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> (days from now).
    /// The provider holds and sends it; nothing in this application sends it by a timer of its own.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Call off a scheduled message before it goes out.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Read the provider's current record for one message (its status and outcome).</summary>
    Task<SmsSendResult> FetchAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider so its text is no longer retrievable there,
    /// while the record that a message was sent and what became of it survives.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending
    /// number over a date range, covering the whole range. Used for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
