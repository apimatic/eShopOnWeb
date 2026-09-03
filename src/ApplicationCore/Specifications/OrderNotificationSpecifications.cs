using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A shopper's orders with their items, for the read-only "my orders" view. No-tracking so the read
/// materializes fresh from the store (in particular, the current order status).
/// </summary>
public sealed class BuyerOrdersWithItemsSpecification : Specification<Order>
{
    public BuyerOrdersWithItemsSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .AsNoTracking();
    }
}

/// <summary>All notifications for one order, oldest first.</summary>
public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        // No-tracking: this drives read endpoints, which must materialize fresh from the store and must
        // not carry write side-effects into the tracker.
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt)
            .AsNoTracking();
    }
}

/// <summary>All notifications belonging to a shopper (across their orders).</summary>
public sealed class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt)
            .AsNoTracking();
    }
}

/// <summary>A single notification by id.</summary>
public sealed class OrderNotificationByIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>A notification previously produced under a given resend idempotency key, if any.</summary>
public sealed class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}

/// <summary>Notifications carrying a provider message id created within a date range (for reconciliation).</summary>
public sealed class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
