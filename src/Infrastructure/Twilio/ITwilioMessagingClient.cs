using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// A hand-written client for the Twilio messaging (v2010) API, built directly to the
/// <c>api-specs/twilio/twilio_api_v2010</c> contract. All calls target the configured
/// messaging base URL (the <c>Twilio:BaseUrl</c> override when set, otherwise the default).
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>Sends a message now from this application's configured sending number.</summary>
    Task<TwilioMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/> via the
    /// Messaging Service (Twilio schedules it; this application holds no timer).
    /// </summary>
    Task<TwilioMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches a single message to read its current delivery outcome.</summary>
    Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<TwilioMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts a message's body at the provider so its text is no longer retrievable.</summary>
    Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every message the provider recorded as sent from <paramref name="fromNumber"/>
    /// over the date range, following pagination to cover the whole range.
    /// </summary>
    Task<IReadOnlyList<TwilioMessage>> ListMessagesByFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
