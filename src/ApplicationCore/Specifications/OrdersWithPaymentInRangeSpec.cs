using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders that carry a PayPal payment and were placed within the reconciliation window. Used to
/// line eShop's payment records up against PayPal's transaction report.
/// </summary>
public class OrdersWithPaymentInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null && o.OrderDate >= from && o.OrderDate <= to);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
