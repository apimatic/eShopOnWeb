using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;

/// <summary>
/// A request to the provider to send one SMS. When <see cref="SendAt"/> is set the message is
/// queued with the provider to be sent at that time rather than immediately.
/// </summary>
public class SendSmsRequest
{
    public SendSmsRequest(string to, string body)
    {
        To = to;
        Body = body;
    }

    /// <summary>Destination in E.164 form.</summary>
    public string To { get; }

    public string Body { get; }

    /// <summary>When set, the provider schedules the message for this time instead of sending now.</summary>
    public DateTimeOffset? SendAt { get; init; }
}
