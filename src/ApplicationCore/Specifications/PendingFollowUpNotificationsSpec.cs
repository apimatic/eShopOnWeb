using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpNotificationsSpec : Specification<OrderNotification>
{
    public PendingFollowUpNotificationsSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null
                         && (n.ProviderStatus == "scheduled"
                             || n.ProviderStatus == "queued"
                             || n.ProviderStatus == "accepted"));
    }
}
