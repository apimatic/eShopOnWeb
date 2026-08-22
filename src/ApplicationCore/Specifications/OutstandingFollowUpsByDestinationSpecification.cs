using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OutstandingFollowUpsByDestinationSpecification : Specification<OrderNotification>
{
    public OutstandingFollowUpsByDestinationSpecification(string buyerId, string destinationE164)
    {
        Query.Where(n =>
            n.BuyerId == buyerId
            && n.DestinationE164 == destinationE164
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && (n.ProviderStatus == "scheduled" || n.ProviderStatus == "accepted"));
    }
}
