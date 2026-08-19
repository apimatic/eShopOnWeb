using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider seen through the lens of the Twilio OpenAPI contract: validate a
/// destination, send a message now, schedule one for later, read a message's delivery outcome,
/// cancel a not-yet-sent message, dispose of a message's content, and list what the configured
/// sender has sent. Implementations build strictly to the <c>api-specs</c> documents.
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's own configured sending number (E.164), used when reconciling.</summary>
    string ConfiguredSender { get; }

    /// <summary>Validates a phone number and returns the provider's canonical E.164 form.</summary>
    Task<PhoneNumberLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the configured sender.</summary>
    Task<GatewayMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a future time.</summary>
    Task<GatewayMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches a message's current state (chiefly its delivery status) from the provider.</summary>
    Task<GatewayMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<GatewayMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text content at the provider (redaction), keeping the record.</summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the messages the provider has for the application's configured sender within the given
    /// date-time range. The sender filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<GatewayMessage>> ListSentByConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The result of validating a phone number with the provider.</summary>
public record PhoneNumberLookup(bool IsValid, string? CanonicalE164);

/// <summary>A message as the provider represents it.</summary>
public record GatewayMessage(
    string Sid,
    string? Status,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body);
