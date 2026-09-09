using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop orders that have a PayPal order attached and were placed within [from, to] — the eShop
/// side of the reconciliation report.
/// </summary>
public class OrdersWithPayPalActivitySpec : Specification<Order>
{
    public OrdersWithPayPalActivitySpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment.PayPalOrderId != null
                        && o.OrderDate >= from
                        && o.OrderDate <= to)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
