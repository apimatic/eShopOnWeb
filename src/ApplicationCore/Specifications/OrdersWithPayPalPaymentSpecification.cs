using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPayPalPaymentSpecification : Specification<Order>
{
    public OrdersWithPayPalPaymentSpecification()
    {
        Query.Where(o => o.Payment.PayPalOrderId != null)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}
