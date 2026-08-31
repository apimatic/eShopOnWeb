using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsByBuyerIdSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerIdSpec(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
