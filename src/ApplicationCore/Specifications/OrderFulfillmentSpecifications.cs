using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderFulfillmentByOrderIdSpec : Specification<OrderFulfillment>
{
    public OrderFulfillmentByOrderIdSpec(int orderId)
    {
        Query.Where(f => f.ForOrderId == orderId);
    }
}

public class OrderFulfillmentsByOrderIdsSpec : Specification<OrderFulfillment>
{
    public OrderFulfillmentsByOrderIdsSpec(int[] orderIds)
    {
        Query.Where(f => orderIds.Contains(f.ForOrderId));
    }
}
