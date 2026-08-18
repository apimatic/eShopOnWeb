using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The boundary over the SMS provider's messaging API. Every method that reaches the provider throws
/// <see cref="Exceptions.SmsGatewayException"/> when the provider rejects the request, is unreachable, or
/// answers with something that cannot be read.
/// </summary>
public interface ITwilioMessagingService
{
    /// <summary>This application's configured sending number (the one reconciliation is scoped to).</summary>
    string ConfiguredFromNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and, if so, for its canonical E.164 form.
    /// Returns an invalid result when the provider does not consider the number usable; throws
    /// <see cref="Exceptions.SmsGatewayException"/> when the provider could not be consulted at all.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Sends an SMS now and returns the provider's identifier and current status for it.</summary>
    Task<MessageDispatchResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<MessageDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Calls off a not-yet-sent scheduled message at the provider.</summary>
    Task<MessageDispatchResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>Reads a single message's current delivery outcome from the provider.</summary>
    Task<MessageDispatchResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's text at the provider so it is no longer retrievable there, while the fact the
    /// message was sent and what became of it survive.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own records of messages sent from this application's configured sending number
    /// over the given date range, covering the whole range. Filtered at the provider by that sending number.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
