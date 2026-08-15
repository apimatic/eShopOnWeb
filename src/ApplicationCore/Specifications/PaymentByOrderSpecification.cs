using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for a single order, including its refunds.</summary>
public sealed class PaymentByOrderSpecification : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments belonging to a buyer, including refunds.</summary>
public sealed class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments (operator/reconciliation), including refunds.</summary>
public sealed class AllPaymentsSpecification : Specification<Payment>
{
    public AllPaymentsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
