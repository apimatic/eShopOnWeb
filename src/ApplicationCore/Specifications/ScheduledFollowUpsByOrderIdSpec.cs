using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsByOrderIdSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderIdSpec(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderStatus == "scheduled" &&
            n.ProviderMessageSid != null);
    }
}
