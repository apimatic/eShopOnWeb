using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every payment belonging to a shopper, with refunds loaded (for GET /api/my-orders).</summary>
public class OrderPaymentsByBuyerSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
