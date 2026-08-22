using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Purpose == NotificationPurpose.DeliveryFollowUp &&
            n.ProviderMessageSid != null);
    }
}
