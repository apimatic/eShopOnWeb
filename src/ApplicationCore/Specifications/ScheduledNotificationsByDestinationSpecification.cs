using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledNotificationsByDestinationSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByDestinationSpecification(string destinationNumber)
    {
        Query.Where(n => n.DestinationNumber == destinationNumber
                && n.IsScheduled
                && n.ProviderMessageSid != null);
    }
}
