using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OutstandingFollowUpNotificationsSpec : Specification<OrderNotification>
{
    public OutstandingFollowUpNotificationsSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DispatchFollowUp
                         && n.ProviderSid != null);
    }
}
