using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class TrackedOrderByOrderIdSpecification : Specification<TrackedOrder>
{
    public TrackedOrderByOrderIdSpecification(int orderId)
    {
        Query.Where(t => t.OrderId == orderId);
    }
}
