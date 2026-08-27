using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for Twilio's messaging API (api.twilio.com, 2010-04-01 Message resource),
/// built to the OpenAPI contract in api-specs/twilio/twilio_api_v2010.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>This application's own configured sending number (Twilio:FromNumber).</summary>
    string FromNumber { get; }

    /// <summary>CreateMessage. When <paramref name="sendAtUtc"/> is set, the message is
    /// queued with the provider for later delivery (ScheduleType=fixed, SendAt).</summary>
    Task<TwilioMessageInfo> SendMessageAsync(string to, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default);

    /// <summary>FetchMessage — current provider state of a single message.</summary>
    Task<TwilioMessageInfo> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>ListMessage — pages through the provider's own record of messages sent
    /// from <paramref name="fromNumber"/> in the given sent-date range.</summary>
    Task<IReadOnlyList<TwilioMessageInfo>> ListMessagesAsync(string fromNumber, DateTimeOffset? dateSentAfter, DateTimeOffset? dateSentBefore, CancellationToken cancellationToken = default);

    /// <summary>UpdateMessage with Status=canceled — calls off a not-yet-sent scheduled message.</summary>
    Task<TwilioMessageInfo> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>UpdateMessage with Body="" — redacts the message text at the provider.</summary>
    Task<TwilioMessageInfo> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}
