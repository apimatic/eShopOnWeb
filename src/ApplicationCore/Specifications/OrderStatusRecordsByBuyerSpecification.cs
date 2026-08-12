using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderStatusRecordsByBuyerSpecification : Specification<OrderStatusRecord>
{
    public OrderStatusRecordsByBuyerSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAt);
    }
}
