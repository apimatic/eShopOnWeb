using System.Collections.Generic;
using System.Linq;
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

public class NotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpecification(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationByIdSpecification : Specification<OrderNotification>
{
    public NotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

public class NotificationByProviderSidSpecification : Specification<OrderNotification>
{
    public NotificationByProviderSidSpecification(string providerMessageSid)
    {
        Query.Where(n => n.ProviderMessageSid == providerMessageSid);
    }
}

public class NotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationResendByIdempotencySpecification : Specification<OrderNotification>
{
    public NotificationResendByIdempotencySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.OriginalNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}

public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == OrderNotificationType.DeliveryFollowUp
            && n.ProviderStatus == "scheduled");
    }
}

public class ScheduledNotificationsByDestinationSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByDestinationSpecification(string buyerId, string destinationNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.DestinationNumber == destinationNumber
            && n.ProviderStatus == "scheduled");
    }
}
