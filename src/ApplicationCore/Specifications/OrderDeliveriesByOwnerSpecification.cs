using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderDeliveriesByOwnerSpecification : Specification<OrderDelivery>
{
    public OrderDeliveriesByOwnerSpecification(string ownerId)
    {
        Query.Where(d => d.OwnerId == ownerId);
    }
}
