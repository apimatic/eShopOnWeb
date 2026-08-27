using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class ActiveContactNumbersByBuyerSpec : Specification<ContactNumber>
{
    public ActiveContactNumbersByBuyerSpec(string buyerId) =>
        Query.Where(x => x.BuyerId == buyerId && x.DeletedAt == null).OrderBy(x => x.Id);
}

public sealed class ActiveContactNumberByOwnerSpec : Specification<ContactNumber>
{
    public ActiveContactNumberByOwnerSpec(int id, string buyerId) =>
        Query.Where(x => x.Id == id && x.BuyerId == buyerId && x.DeletedAt == null);
}

public sealed class NotificationsByOrderSpec : Specification<OrderNotification>
{
    public NotificationsByOrderSpec(int orderId) => Query.Where(x => x.OrderId == orderId).OrderBy(x => x.Id);
}

public sealed class ScheduledNotificationsByContactSpec : Specification<OrderNotification>
{
    public ScheduledNotificationsByContactSpec(int contactNumberId) =>
        Query.Where(x => x.ContactNumberId == contactNumberId && x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null);
}

public sealed class ResendByKeySpec : Specification<OrderNotification>
{
    public ResendByKeySpec(int originalNotificationId, string idempotencyKey) =>
        Query.Where(x => x.ResendOfNotificationId == originalNotificationId && x.IdempotencyKey == idempotencyKey);
}

public sealed class CustomerOrdersWithNotificationsSpec : Specification<Order>
{
    public CustomerOrdersWithNotificationsSpec(string buyerId) =>
        Query.Where(x => x.BuyerId == buyerId).Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate);
}

public sealed class NotificationsByBuyerSpec : Specification<OrderNotification>
{
    public NotificationsByBuyerSpec(string buyerId) => Query.Where(x => x.BuyerId == buyerId);
}

public sealed class NotificationsInRangeSpec : Specification<OrderNotification>
{
    public NotificationsInRangeSpec(DateTimeOffset from, DateTimeOffset to) =>
        Query.Where(x => x.CreatedAt >= from && x.CreatedAt <= to && x.ProviderMessageSid != null);
}
