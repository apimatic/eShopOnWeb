using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconcileNotificationsRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationMatch> Matched { get; set; } = new();
    public List<ReconciliationProviderMessage> ProviderOnly { get; set; } = new();
    public List<ReconciliationApplicationMessage> ApplicationOnly { get; set; } = new();
}
