using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

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
    public IReadOnlyList<NotificationReconciliationItem> Matched { get; set; } = Array.Empty<NotificationReconciliationItem>();
    public IReadOnlyList<NotificationReconciliationItem> ProviderOnly { get; set; } = Array.Empty<NotificationReconciliationItem>();
    public IReadOnlyList<NotificationReconciliationItem> LocalOnly { get; set; } = Array.Empty<NotificationReconciliationItem>();
}
