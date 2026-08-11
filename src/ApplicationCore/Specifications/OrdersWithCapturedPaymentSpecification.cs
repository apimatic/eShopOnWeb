using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders placed within a date range that have a captured payment — the eShop side of reconciliation.
/// </summary>
public class OrdersWithCapturedPaymentSpecification : Specification<Order>
{
    public OrdersWithCapturedPaymentSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.OrderDate >= from && o.OrderDate <= to
                && o.Payment != null && o.Payment.CaptureId != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
