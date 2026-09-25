using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpec : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        // Refunds are an owned collection and are loaded automatically with the Payment.
        Query.Where(p => p.OrderId == orderId);
    }
}

/// <summary>All payments belonging to a shopper (for the my-orders projection).</summary>
public class PaymentsByBuyerSpec : Specification<Payment>
{
    public PaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}
