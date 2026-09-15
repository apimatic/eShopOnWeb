using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId).OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByOrdersSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrdersSpecification(int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId)).OrderBy(n => n.OrderId).ThenBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null);
    }
}

public class ScheduledFollowUpsByContactSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByContactSpecification(int contactNumberId)
    {
        Query.Where(n =>
            n.ContactNumberId == contactNumberId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null);
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class OrderNotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpecification(string[] providerSids)
    {
        Query.Where(n => n.ProviderMessageSid != null && providerSids.Contains(n.ProviderMessageSid));
    }
}
