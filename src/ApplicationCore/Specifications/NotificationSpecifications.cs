using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's own registered contact numbers.</summary>
public class ContactNumbersByOwnerSpec : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpec(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.CreatedDate);
    }
}

/// <summary>A single contact number scoped to its owner, so one shopper can never act on
/// another's number.</summary>
public class ContactNumberByIdForOwnerSpec : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpec(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}

/// <summary>Every notification sent about a given order.</summary>
public class OrderNotificationsByOrderSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedDate);
    }
}

/// <summary>Look up a notification produced under a specific idempotency key.</summary>
public class OrderNotificationByIdempotencyKeySpec : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpec(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>All notifications for a set of orders (used to summarise a shopper's orders).</summary>
public class OrderNotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpec(int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedDate);
    }
}
