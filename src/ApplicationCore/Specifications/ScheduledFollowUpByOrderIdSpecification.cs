using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpByOrderIdSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpByOrderIdSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.ProviderStatus == "scheduled" &&
            n.ProviderSid != null);
    }
}
