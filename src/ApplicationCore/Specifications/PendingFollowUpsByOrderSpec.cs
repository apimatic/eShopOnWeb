using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderSid != null
            && n.Status != "canceled"
            && n.Status != "cancelled"
            && n.Status != "sent"
            && n.Status != "delivered"
            && n.Status != "undelivered"
            && n.Status != "failed");
    }
}
