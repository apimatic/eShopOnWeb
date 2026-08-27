using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class ActiveContactNumbersByBuyerSpec : Specification<ContactNumber>
{
    public ActiveContactNumbersByBuyerSpec(string buyerId)
    {
        Query.Where(number => number.BuyerId == buyerId && number.RemovedAt == null)
            .OrderBy(number => number.Id);
    }
}

public sealed class ActiveContactNumberByOwnerAndIdSpec : Specification<ContactNumber>
{
    public ActiveContactNumberByOwnerAndIdSpec(string buyerId, int id)
    {
        Query.Where(number => number.Id == id && number.BuyerId == buyerId && number.RemovedAt == null);
    }
}

public sealed class ActiveContactNumberByOwnerAndValueSpec : Specification<ContactNumber>
{
    public ActiveContactNumberByOwnerAndValueSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(number => number.BuyerId == buyerId &&
            number.CanonicalNumber == canonicalNumber && number.RemovedAt == null);
    }
}

public sealed class NotificationsByOrderSpec : Specification<OrderNotification>
{
    public NotificationsByOrderSpec(int orderId)
    {
        Query.Where(notification => notification.OrderId == orderId)
            .OrderBy(notification => notification.CreatedAt);
    }
}

public sealed class PendingFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(notification => notification.OrderId == orderId &&
            notification.Kind == NotificationKind.DeliveryFollowUp &&
            notification.ProviderMessageSid != null &&
            notification.ProviderStatus != "canceled" &&
            notification.ProviderStatus != "delivered" &&
            notification.ProviderStatus != "sent" &&
            notification.ProviderStatus != "failed" &&
            notification.ProviderStatus != "undelivered");
    }
}

public sealed class PendingFollowUpsByContactSpec : Specification<OrderNotification>
{
    public PendingFollowUpsByContactSpec(int contactNumberId)
    {
        Query.Where(notification => notification.ContactNumberId == contactNumberId &&
            notification.Kind == NotificationKind.DeliveryFollowUp &&
            notification.ProviderMessageSid != null &&
            notification.ProviderStatus != "canceled" &&
            notification.ProviderStatus != "delivered" &&
            notification.ProviderStatus != "sent" &&
            notification.ProviderStatus != "failed" &&
            notification.ProviderStatus != "undelivered");
    }
}

public sealed class ResendByKeySpec : Specification<OrderNotification>
{
    public ResendByKeySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(notification => notification.ResendOfNotificationId == originalNotificationId &&
            notification.IdempotencyKey == idempotencyKey);
    }
}

public sealed class NotificationsCreatedInRangeSpec : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(notification => notification.CreatedAt >= from && notification.CreatedAt <= to)
            .OrderBy(notification => notification.CreatedAt);
    }
}
