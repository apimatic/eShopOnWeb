using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderSpec : Specification<OrderNotification>
{
    public NotificationsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationsByBuyerAndOrderSpec : Specification<OrderNotification>
{
    public NotificationsByBuyerAndOrderSpec(string buyerId, int orderId)
    {
        Query.Where(n => n.BuyerId == buyerId && n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToList();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}

public class PendingFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && (n.ProviderStatus == "scheduled"
                || n.ProviderStatus == "accepted"
                || n.ProviderStatus == "queued"
                || n.ProviderStatus == "sending"));
    }
}

public class PendingFollowUpsByDestinationSpec : Specification<OrderNotification>
{
    public PendingFollowUpsByDestinationSpec(string destinationNumber)
    {
        Query.Where(n => n.DestinationNumber == destinationNumber
            && n.Kind == NotificationKind.DeliveryFollowUp
            && (n.ProviderStatus == "scheduled"
                || n.ProviderStatus == "accepted"
                || n.ProviderStatus == "queued"
                || n.ProviderStatus == "sending"));
    }
}

public class NotificationsWithProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsWithProviderSidsSpec()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}

public class ResendAttemptByKeySpec : Specification<NotificationResendAttempt>, ISingleResultSpecification<NotificationResendAttempt>
{
    public ResendAttemptByKeySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(a => a.OriginalNotificationId == originalNotificationId && a.IdempotencyKey == idempotencyKey);
    }
}
