using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OutstandingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public OutstandingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && (n.ProviderStatus == "scheduled" || n.ProviderStatus == "accepted"));
    }
}
