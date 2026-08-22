using System;

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
