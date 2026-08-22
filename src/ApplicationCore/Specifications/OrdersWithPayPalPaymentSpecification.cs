using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPayPalPaymentSpecification : Specification<Order>
{
    public OrdersWithPayPalPaymentSpecification()
    {
        Query.Where(o => o.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            .Include(o => o.Refunds);
    }
}
