using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledNotificationsByDestinationSpec : Specification<OrderNotification>
{
    public ScheduledNotificationsByDestinationSpec(string destinationPhoneNumber)
    {
        Query.Where(n =>
            n.DestinationPhoneNumber == destinationPhoneNumber &&
            n.ProviderStatus == "scheduled" &&
            n.ProviderMessageSid != null);
    }
}
