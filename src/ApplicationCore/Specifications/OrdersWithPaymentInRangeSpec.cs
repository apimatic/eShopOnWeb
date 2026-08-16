using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders whose payment activity may fall in a reconciliation window: placed within the range and
/// carrying a PayPal order id. Used to line eShop records up against PayPal's own transactions.
/// </summary>
public class OrdersWithPaymentInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Payment != null && o.Payment.PayPalOrderId != null);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
