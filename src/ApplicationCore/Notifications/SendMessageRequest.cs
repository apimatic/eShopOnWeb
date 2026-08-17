using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A request to send or schedule one message. The sending number and Messaging Service are
/// supplied by the client from configuration; callers only provide destination, text and an
/// optional future send time.
/// </summary>
public class SendMessageRequest
{
    public SendMessageRequest(string to, string body, DateTimeOffset? sendAt = null)
    {
        To = to;
        Body = body;
        SendAt = sendAt;
    }

    /// <summary>Canonical E.164 destination.</summary>
    public string To { get; }

    public string Body { get; }

    /// <summary>
    /// When set, the message is scheduled with the provider for this time (via the Messaging
    /// Service) rather than sent immediately.
    /// </summary>
    public DateTimeOffset? SendAt { get; }
}
