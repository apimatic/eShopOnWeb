using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            (n.ProviderStatus == "scheduled" || n.ProviderStatus == "queued" || n.ProviderStatus == "accepted" || n.ProviderStatus == "pending"));
    }
}

public class ScheduledNotificationsByDestinationSpec : Specification<OrderNotification>
{
    public ScheduledNotificationsByDestinationSpec(string destinationNumber)
    {
        Query.Where(n =>
            n.DestinationNumber == destinationNumber &&
            n.ProviderMessageSid != null &&
            (n.ProviderStatus == "scheduled" || n.ProviderStatus == "queued" || n.ProviderStatus == "accepted" || n.ProviderStatus == "pending"));
    }
}

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IEnumerable<string> sids)
    {
        var sidList = sids.ToList();
        Query.Where(n => n.ProviderMessageSid != null && sidList.Contains(n.ProviderMessageSid));
    }
}

public class NotificationsInCreatedRangeSpec : Specification<OrderNotification>
{
    public NotificationsInCreatedRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class ResendRecordByKeySpec : Specification<NotificationResendRecord>
{
    public ResendRecordByKeySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
