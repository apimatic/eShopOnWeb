using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentsSpecification : Specification<Order>
{
    public OrdersWithPaymentsSpecification()
    {
        Query
            .Where(o => o.PayPalOrderId != null || o.AuthorizationId != null || o.CaptureId != null)
            .Include(o => o.Refunds);
    }
}
