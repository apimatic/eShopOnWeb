using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CancellableFollowUpsByDestinationSpecification : Specification<OrderNotification>
{
    public CancellableFollowUpsByDestinationSpecification(string destination)
    {
        Query.Where(n => n.Destination == destination
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null);
    }
}
