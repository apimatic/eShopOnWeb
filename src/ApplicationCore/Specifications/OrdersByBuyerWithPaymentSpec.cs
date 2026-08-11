using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The caller's own orders, newest first, with items and refunds so their payment state can be reported.
/// </summary>
public class OrdersByBuyerWithPaymentSpec : Specification<Order>
{
    public OrdersByBuyerWithPaymentSpec(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query.Include(o => o.Refunds);
        Query.OrderByDescending(o => o.OrderDate);
    }
}

/// <summary>
/// All captured orders in a date range (used by reconciliation to line eShop up against PayPal).
/// </summary>
public class CapturedOrdersInRangeSpec : Specification<Order>
{
    public CapturedOrdersInRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query
            .Where(o => o.PayPalCaptureId != null && o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.Refunds);
    }
}
