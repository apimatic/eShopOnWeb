using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public class SmsSendRequest
{
    public SmsSendRequest(string to, string body, DateTimeOffset? sendAt = null)
    {
        To = to;
        Body = body;
        SendAt = sendAt;
    }

    public string To { get; }
    public string Body { get; }
    public DateTimeOffset? SendAt { get; }
}
