using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class FollowUpByOrderSpecification : Specification<OrderNotification>
{
    public FollowUpByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == NotificationKind.DeliveryFollowUp)
            .OrderByDescending(n => n.Id);
    }
}
