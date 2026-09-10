using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for a single order, with its refunds loaded.</summary>
public sealed class OrderPaymentByOrderIdSpecification : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments owned by a shopper, with refunds loaded.</summary>
public sealed class OrderPaymentsByBuyerSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>Every payment that has reached PayPal (has a processor order id), for reconciliation.</summary>
public sealed class ProcessedPaymentsSpecification : Specification<OrderPayment>
{
    public ProcessedPaymentsSpecification()
    {
        Query.Where(p => p.ProcessorOrderId != null)
            .Include(p => p.Refunds);
    }
}
