using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpNotificationSpecification : Specification<OrderNotification>
{
    public PendingFollowUpNotificationSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null);
    }
}
