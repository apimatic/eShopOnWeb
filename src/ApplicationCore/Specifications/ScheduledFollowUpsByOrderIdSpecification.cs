using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsByOrderIdSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DeliveryFeedback
                         && n.ProviderSid != null);
    }
}
