using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for a single order, with its refunds loaded.</summary>
public class OrderPaymentByOrderIdSpecification : Specification<OrderPayment>
{
    public OrderPaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments belonging to one shopper, with refunds loaded.</summary>
public class OrderPaymentsByBuyerSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
