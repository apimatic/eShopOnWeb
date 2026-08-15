using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop orders that carry a payment and were placed within a date range — the eShop side of the
/// reconciliation report.
/// </summary>
public class OrdersWithPaymentInRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null && o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
