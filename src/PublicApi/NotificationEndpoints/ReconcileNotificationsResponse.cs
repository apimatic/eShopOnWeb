using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsRequest : BaseRequest
{
    public ReconcileNotificationsRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconcileNotificationsResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public bool Truncated { get; set; }
    public List<MatchedNotification> Matched { get; set; } = new();
    public List<SmsMessageResult> ProviderOnly { get; set; } = new();
    public List<ApplicationOnlyNotification> ApplicationOnly { get; set; } = new();
}
