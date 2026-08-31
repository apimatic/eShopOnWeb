using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the Twilio messaging API (api.twilio.com, 2010-04-01 Messages),
/// built against the Twilio OpenAPI specification in api-specs/twilio.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>CreateMessage. When sendAt is set the message is scheduled with the
    /// provider (ScheduleType=fixed), which requires a Messaging Service.</summary>
    Task<TwilioMessage> CreateMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    /// <summary>FetchMessage.</summary>
    Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>UpdateMessage with Status=canceled, for a not-yet-sent (scheduled) message.</summary>
    Task<TwilioMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>UpdateMessage with an empty Body, redacting the message text at the provider.</summary>
    Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>ListMessage for this application's own sending number over a date range,
    /// following every page so the whole range is covered.</summary>
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
