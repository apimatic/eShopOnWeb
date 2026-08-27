using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Kind == NotificationKind.DispatchFollowUp
            && n.ProviderSid != null
            && n.ProviderStatus != "delivered"
            && n.ProviderStatus != "sent"
            && n.ProviderStatus != "undelivered"
            && n.ProviderStatus != "failed"
            && n.ProviderStatus != "canceled"
            && n.ProviderStatus != "read");
    }
}
