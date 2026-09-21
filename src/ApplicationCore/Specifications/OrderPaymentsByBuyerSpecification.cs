using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsByBuyerSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
