using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderSid != null
            && n.ProviderStatus != "canceled"
            && n.ProviderStatus != "sent"
            && n.ProviderStatus != "delivered"
            && n.ProviderStatus != "failed"
            && n.ProviderStatus != "undelivered");
    }
}
