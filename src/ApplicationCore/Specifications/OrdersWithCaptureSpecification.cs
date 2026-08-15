using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders that have a PayPal capture whose order date falls in a range, with their refunds. Backs
/// the reconciliation report's "eShop side" — the orders whose money actually moved, to line up
/// against PayPal's ledger for the same window.
/// </summary>
public class OrdersWithCaptureSpecification : Specification<Order>
{
    public OrdersWithCaptureSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.PayPalCaptureId != null && o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.Refunds);
    }
}
