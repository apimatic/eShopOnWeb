using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider. Keeps the provider's SDK out of the application and domain
/// layers: only plain DTOs cross this boundary. The concrete implementation lives in Infrastructure.
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's own configured sending number (E.164), as seen by the provider.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical form.
    /// </summary>
    Task<PhoneNumberValidation> ValidateAsync(string rawNumber, CancellationToken ct);

    /// <summary>Sends a message now, from the configured sending number.</summary>
    Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken ct);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/>. The provider owns the
    /// timing; nothing is held in this application to be released by a timer of its own.
    /// </summary>
    Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Calls off a message that the provider has not yet sent.</summary>
    Task CancelScheduledAsync(string providerSid, CancellationToken ct);

    /// <summary>Reads the provider's current record for a message.</summary>
    Task<SmsDeliveryState> GetDeliveryStateAsync(string providerSid, CancellationToken ct);

    /// <summary>
    /// Disposes of a message's text at the provider so it can no longer be retrieved there, while the
    /// fact that the message was sent, and its outcome, survive.
    /// </summary>
    Task RedactContentAsync(string providerSid, CancellationToken ct);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number in a date
    /// range. The sending-number filter is applied at the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Outcome of validating a candidate destination number.</summary>
public record PhoneNumberValidation(bool IsUsable, string? CanonicalNumber);

/// <summary>What the provider returned when it accepted a message for sending or scheduling.</summary>
public record SmsDispatchResult(string ProviderSid, string? From, string? Status, DateTimeOffset? DateSent);

/// <summary>A later read of the provider's record for a message.</summary>
public record SmsDeliveryState(string? Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateSent, string? From);

/// <summary>One row of the provider's own message log, used for reconciliation.</summary>
public record ProviderMessageRecord(string ProviderSid, string? To, string? From, string? Status, DateTimeOffset? DateSent);
