using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderSid != null);
    }
}
