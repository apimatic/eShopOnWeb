using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin client over Twilio's HTTP API, built to the OpenAPI specifications in <c>api-specs/</c>.
/// Lookups use the Lookups v2 API (host lookups.twilio.com); everything else uses the 2010-04-01
/// messaging API (host api.twilio.com, overridable by <c>Twilio:BaseUrl</c>).
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>
    /// Look a number up (Lookups v2) to decide whether it is a usable destination and to obtain the
    /// provider's canonical E.164 form. Returns <see cref="PhoneNumberLookupResult.Valid"/> = false
    /// for a number the provider does not consider valid.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create and send an SMS immediately (from the configured FromNumber). Returns the created
    /// message resource. Throws <see cref="TwilioApiException"/> if the provider rejects the request
    /// outright (a message that is accepted but later refused by the carrier is NOT an error here —
    /// it surfaces later as an <c>undelivered</c>/<c>failed</c> status).
    /// </summary>
    Task<TwilioMessageResource> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue an SMS with the provider to be sent at <paramref name="sendAt"/> (via the Messaging
    /// Service, ScheduleType=fixed). The provider holds and later sends it; this application does not.
    /// </summary>
    Task<TwilioMessageResource> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetch a message's current state from the provider (delivery outcome, error, date sent).</summary>
    Task<TwilioMessageResource> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message with the provider so it never goes out.</summary>
    Task<TwilioMessageResource> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redact a message's body at the provider (Body=''), so the text is no longer retrievable there
    /// while the record of the message — and what became of it — survives.
    /// </summary>
    Task<TwilioMessageResource> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from <paramref name="fromE164"/> within
    /// [<paramref name="dateSentFrom"/>, <paramref name="dateSentTo"/>], following pagination so the
    /// whole range is covered. Filtering by sender is asked of the provider, not applied afterwards.
    /// </summary>
    Task<IReadOnlyList<TwilioMessageResource>> ListMessagesFromNumberAsync(
        string fromE164, DateTimeOffset dateSentFrom, DateTimeOffset dateSentTo, CancellationToken cancellationToken = default);
}
