using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledNotificationsToNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsToNumberSpecification(string canonicalNumber)
    {
        Query.Where(n => n.DestinationNumber == canonicalNumber
                         && n.ProviderMessageSid != null
                         && n.ProviderStatus == "scheduled");
    }
}
