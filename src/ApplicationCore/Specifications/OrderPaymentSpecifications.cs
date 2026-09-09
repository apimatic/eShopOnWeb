using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The single payment record for one order, with its refunds loaded.</summary>
public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All of one shopper's payments, most recent first, with refunds loaded.</summary>
public class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.CreatedAt);
    }
}

/// <summary>Every payment (operator scope) — used to line PayPal transactions up against eShop.</summary>
public class AllOrderPaymentsSpec : Specification<OrderPayment>
{
    public AllOrderPaymentsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
