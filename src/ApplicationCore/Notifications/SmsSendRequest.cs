using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A request to send one message. The sending number / messaging service are supplied by the provider
/// integration from configuration, not by callers.
/// </summary>
public class SmsSendRequest
{
    public SmsSendRequest(string to, string body, DateTimeOffset? scheduleFor = null)
    {
        To = to;
        Body = body;
        ScheduleFor = scheduleFor;
    }

    /// <summary>Destination number in E.164 form.</summary>
    public string To { get; }

    /// <summary>Message text.</summary>
    public string Body { get; }

    /// <summary>When set, the provider is asked to send the message at this time rather than immediately.</summary>
    public DateTimeOffset? ScheduleFor { get; }
}
