using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class DeliveryFollowUpByOrderSpec : Specification<OrderNotification>
{
    public DeliveryFollowUpByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == Notifications.NotificationKinds.DeliveryFollowUp)
            .OrderByDescending(n => n.Id);
    }
}
