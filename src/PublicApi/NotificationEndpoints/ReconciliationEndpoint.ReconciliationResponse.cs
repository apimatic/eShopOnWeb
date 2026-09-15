using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<NotificationReconciliationItem> Matched { get; set; } = new();
    public List<NotificationReconciliationItem> ProviderOnly { get; set; } = new();
    public List<NotificationReconciliationItem> LocalOnly { get; set; } = new();
}
