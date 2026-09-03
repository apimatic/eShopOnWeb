using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CancellableOrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public CancellableOrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.ScheduledFor != null
            && n.ProviderMessageSid != null
            && n.CancellationCompletedAt == null);
    }
}
