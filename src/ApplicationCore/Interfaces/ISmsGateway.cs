using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The abstraction over the SMS provider. The only seam through which the application talks to Twilio; the
/// implementation lives in Infrastructure and is the sole holder of the SDK. Every method converts provider
/// failures into <see cref="Exceptions.SmsGatewayException"/> and never logs a destination number or body.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Ask the provider whether a caller-typed number is a usable destination, and for its canonical form.
    /// </summary>
    Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, CancellationToken ct);

    /// <summary>Send a message now, from the application's configured sending number.</summary>
    Task<ProviderMessageResult> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> (a fixed schedule via the
    /// configured messaging service) — not held in this application.
    /// </summary>
    Task<ProviderMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Read the provider's current record of a message by its identifier.</summary>
    Task<ProviderMessageResult> FetchAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Cancel a scheduled message that has not yet gone out.</summary>
    Task<ProviderMessageResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's text at the provider so it is no longer retrievable there, while the record of
    /// the message and its outcome survive.
    /// </summary>
    Task DisposeContentAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// List the provider's own record of messages sent from the application's configured sending number in a
    /// date range, asking the provider to filter by that number rather than filtering a wider answer here.
    /// </summary>
    Task<ProviderMessageListing> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Result of a provider phone-number validation/lookup.</summary>
public record PhoneNumberValidation(bool IsValid, string? CanonicalE164);

/// <summary>The state of a single message as the provider reported it on a send/schedule/fetch/cancel.</summary>
public record ProviderMessageResult(
    string? Sid,
    string? RawStatus,
    NotificationDeliveryState State,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>One row of the provider's own message log, as returned by a range listing.</summary>
public record ProviderMessageSummary(
    string Sid,
    string? RawStatus,
    string? From,
    DateTimeOffset? DateSent);

/// <summary>A range listing of provider messages, flagged when it was capped before covering the range.</summary>
public record ProviderMessageListing(IReadOnlyList<ProviderMessageSummary> Messages, bool Truncated);
