using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin abstraction over the Twilio HTTP APIs used by this integration. Implemented in the
/// Infrastructure layer strictly against the OpenAPI documents in <c>api-specs/twilio</c>:
/// Lookups v2 for number validation and the 2010-04-01 Messaging API for sending, reading,
/// cancelling, redacting and listing messages.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>
    /// Validates a phone number with the provider and returns its canonical form.
    /// Uses Lookups v2 (<c>GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}</c>).
    /// </summary>
    Task<PhoneNumberLookupResult> LookupPhoneNumberAsync(string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends (or schedules) a message. When <see cref="SendMessageRequest.SendAt"/> is set the
    /// message is queued with the provider for that time via the configured Messaging Service.
    /// </summary>
    Task<TwilioMessageResource> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetches the current provider state of a message (<c>GET Messages/{Sid}.json</c>).</summary>
    Task<TwilioMessageResource> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message (<c>POST Messages/{Sid}.json Status=canceled</c>).</summary>
    Task<TwilioMessageResource> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's body at the provider so the text is no longer retrievable there
    /// (<c>POST Messages/{Sid}.json Body=</c> empty). The message record and its outcome survive.
    /// </summary>
    Task<TwilioMessageResource> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's messages sent from this application's configured sending number within
    /// the given date range, following pagination to cover the whole range. The provider is asked
    /// for that number's messages via the server-side <c>From</c> filter.
    /// </summary>
    Task<IReadOnlyList<TwilioMessageResource>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
