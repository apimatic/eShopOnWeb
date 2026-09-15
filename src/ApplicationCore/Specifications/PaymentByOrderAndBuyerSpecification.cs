using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for an order scoped to its owning shopper, with its refunds loaded.</summary>
public class PaymentByOrderAndBuyerSpecification : Specification<Payment>
{
    public PaymentByOrderAndBuyerSpecification(int orderId, string buyerId)
    {
        Query.Where(p => p.OrderId == orderId && p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
