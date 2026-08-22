using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpec : Specification<ShopperContactNumber>
{
    public ContactNumbersByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderByDescending(n => n.Id);
    }
}

public class ContactNumberByCanonicalSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.CanonicalNumber == canonicalNumber);
    }
}

public class LatestContactNumberByBuyerSpec : Specification<ShopperContactNumber>
{
    public LatestContactNumberByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderByDescending(n => n.Id)
            .Take(1);
    }
}

public class OrderNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByBuyerSpec : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderSid != null &&
            n.ProviderStatus == "scheduled");
    }
}

public class NotificationsWithProviderSidInRangeSpec : Specification<OrderNotification>
{
    public NotificationsWithProviderSidInRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to && n.ProviderSid != null);
    }
}

public class ResendIdempotencyByKeySpec : Specification<NotificationResendIdempotency>
{
    public ResendIdempotencyByKeySpec(string idempotencyKey)
    {
        Query.Where(r => r.IdempotencyKey == idempotencyKey);
    }
}
