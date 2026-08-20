using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledNotificationsByDestinationSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByDestinationSpecification(string buyerId, string destinationE164)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.DestinationE164 == destinationE164
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null);
    }
}
