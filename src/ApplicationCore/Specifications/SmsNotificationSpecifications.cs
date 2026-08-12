using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification for one order, oldest first.</summary>
public sealed class OrderNotificationsSpecification : Specification<SmsNotification>
{
    public OrderNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>Every notification for a set of orders (used to summarise a shopper's orders).</summary>
public sealed class NotificationsByOrderIdsSpecification : Specification<SmsNotification>
{
    public NotificationsByOrderIdsSpecification(int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>A prior resend carrying a caller-supplied idempotency key, so a repeat under the same key does not send again.</summary>
public sealed class NotificationByIdempotencyKeySpecification : Specification<SmsNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
