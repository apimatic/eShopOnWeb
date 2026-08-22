using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledNotificationsByDestinationSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByDestinationSpecification(string buyerId, string destinationPhoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
                         && n.DestinationPhoneNumber == destinationPhoneNumber
                         && n.ProviderSid != null
                         && n.Kind == OrderNotificationKind.DeliveryFeedback);
    }
}
