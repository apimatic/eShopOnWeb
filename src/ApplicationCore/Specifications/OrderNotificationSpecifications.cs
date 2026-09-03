using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class OrderNotificationByIdSpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public OrderNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

public class ResendByIdempotencyKeySpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public ResendByIdempotencyKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == originalNotificationId
                         && n.ResendIdempotencyKey == idempotencyKey);
    }
}

public class NotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}
