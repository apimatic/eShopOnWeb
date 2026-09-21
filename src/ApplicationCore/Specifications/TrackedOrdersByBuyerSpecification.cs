using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class TrackedOrdersByBuyerSpecification : Specification<TrackedOrder>
{
    public TrackedOrdersByBuyerSpecification(string buyerId)
    {
        Query.Where(t => t.BuyerId == buyerId);
    }
}
