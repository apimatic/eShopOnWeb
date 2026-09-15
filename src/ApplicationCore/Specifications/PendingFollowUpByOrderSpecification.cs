using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            (n.ProviderStatus == "scheduled" ||
             n.ProviderStatus == "queued" ||
             n.ProviderStatus == "accepted" ||
             n.ProviderStatus == "pending"));
    }
}
