using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpec(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}

public class CancelableFollowUpsByOrderIdSpec : Specification<OrderNotification>
{
    public CancelableFollowUpsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null
                         && (n.ProviderStatus == "scheduled"
                             || n.ProviderStatus == "queued"
                             || n.ProviderStatus == "accepted"
                             || n.ProviderStatus == "sending"));
    }
}

public class NotificationsInRangeSpec : Specification<OrderNotification>
{
    public NotificationsInRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IReadOnlyCollection<string> sids)
    {
        Query.Where(n => n.ProviderMessageSid != null && sids.Contains(n.ProviderMessageSid));
    }
}
