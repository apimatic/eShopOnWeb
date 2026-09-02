using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentDetailsSpecification : Specification<Order>
{
    public OrderWithPaymentDetailsSpecification(int orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}

public class OrdersByBuyerSpecification : Specification<Order>
{
    public OrdersByBuyerSpecification(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}

/// <summary>
/// All orders that carry PayPal payment state, regardless of when they were placed,
/// so reconciliation can line up any PayPal transaction against them.
/// </summary>
public class OrdersWithPaymentsSpecification : Specification<Order>
{
    public OrdersWithPaymentsSpecification()
    {
        Query
            .Where(o => o.PayPalOrderId != null)
            .Include(o => o.Refunds);
    }
}
