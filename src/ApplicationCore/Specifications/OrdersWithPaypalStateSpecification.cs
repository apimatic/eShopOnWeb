using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaypalStateSpecification : Specification<Order>
{
    public OrdersWithPaypalStateSpecification()
    {
        Query.Where(o => o.PaypalOrderId != null || o.AuthorizationId != null || o.CaptureId != null)
            .Include(o => o.Refunds);
    }
}
