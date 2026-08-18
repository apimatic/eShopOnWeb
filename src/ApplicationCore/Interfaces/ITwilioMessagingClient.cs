using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider (Twilio) as seen by the application. Every method maps to an operation defined in
/// the Twilio OpenAPI specification: phone-number lookup (Lookups v2) and the Message resource
/// (Api 2010-04-01) for sending, scheduling, fetching, cancelling, redacting and listing messages.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>
    /// Looks a number up with the provider (Lookups v2). Returns whether the provider considers it a
    /// usable destination and, when valid, the provider's own canonical E.164 form of the number.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now to <paramref name="to"/> from the configured sending number.</summary>
    Task<TwilioMessageResource> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/> (the delivery follow-up).
    /// Scheduling is done by the provider, not by any timer inside this application.
    /// </summary>
    Task<TwilioMessageResource> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message, including its delivery outcome.</summary>
    Task<TwilioMessageResource> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Tells the provider to cancel a not-yet-sent (scheduled) message.</summary>
    Task<TwilioMessageResource> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so the text is no longer retrievable there.</summary>
    Task<TwilioMessageResource> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="fromNumber"/> whose sent date
    /// falls in the inclusive [<paramref name="fromDateUtc"/>, <paramref name="toDateUtc"/>] range, following
    /// pagination so the whole range is covered.
    /// </summary>
    Task<IReadOnlyList<TwilioMessageResource>> ListByFromAsync(string fromNumber, DateTimeOffset fromDateUtc, DateTimeOffset toDateUtc, CancellationToken cancellationToken = default);
}

/// <summary>The provider's answer to a phone-number lookup.</summary>
public record PhoneNumberLookupResult(bool Valid, string? PhoneNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>A projection of the provider's Message resource carrying the fields this integration needs.</summary>
public record TwilioMessageResource(
    string Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateUpdated);
