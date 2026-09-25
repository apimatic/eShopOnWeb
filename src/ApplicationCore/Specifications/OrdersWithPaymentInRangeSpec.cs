using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders placed within a date range that have a payment attempted against them — the eShop side of
/// the reconciliation report.
/// </summary>
public class OrdersWithPaymentInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Payment != null)
            .OrderBy(o => o.OrderDate);
    }
}
