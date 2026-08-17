using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment (with its refunds) for a single order.</summary>
public class PaymentByOrderSpecification : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments (with refunds) owned by a shopper.</summary>
public class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>Every payment that has been captured or refunded (candidates for reconciliation).</summary>
public class SettledPaymentsSpecification : Specification<Payment>
{
    public SettledPaymentsSpecification()
    {
        Query.Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
