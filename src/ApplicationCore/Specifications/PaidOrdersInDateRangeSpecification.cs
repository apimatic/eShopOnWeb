using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders that have a payment and whose order date falls in a range — the eShop side of a
/// reconciliation report.
/// </summary>
public class PaidOrdersInDateRangeSpecification : Specification<Order>
{
    public PaidOrdersInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null && o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
