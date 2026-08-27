using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Twilio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the Twilio messaging API (api.twilio.com), built against the
/// twilio_api_v2010 OpenAPI document: CreateMessage, ListMessage, FetchMessage
/// and UpdateMessage on /2010-04-01/Accounts/{AccountSid}/Messages[.json].
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>
    /// CreateMessage. When <paramref name="sendAt"/> is provided the message is
    /// queued with the provider for later delivery (ScheduleType=fixed, SendAt),
    /// which requires the configured Messaging Service.
    /// </summary>
    Task<TwilioMessage> CreateMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);

    /// <summary>FetchMessage.</summary>
    Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// ListMessage, restricted at the provider to messages sent From the
    /// application's own configured sending number within [from, to).
    /// Pages through the whole range.
    /// </summary>
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>UpdateMessage with Status=canceled; only valid for not-yet-sent (scheduled) messages.</summary>
    Task<TwilioMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>UpdateMessage with Body="" — redacts the message text at the provider.</summary>
    Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}
