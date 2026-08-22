using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciledNotification> Matched { get; set; } = new();
    public List<ProviderOnlyNotification> ProviderOnly { get; set; } = new();
    public List<ApplicationOnlyNotification> ApplicationOnly { get; set; } = new();
}
