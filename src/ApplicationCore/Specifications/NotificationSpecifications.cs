using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class FollowUpNotificationsByOrderIdSpecification : Specification<OrderNotification>
{
    public FollowUpNotificationsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == OrderNotificationKind.DeliveryFollowUp);
    }
}

public class NotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}

public class ResendKeySpecification : Specification<NotificationResendKey>
{
    public ResendKeySpecification(int notificationId, string idempotencyKey)
    {
        Query.Where(k => k.NotificationId == notificationId && k.IdempotencyKey == idempotencyKey);
    }
}
