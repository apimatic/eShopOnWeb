using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders that have a payment, placed within a date range. Used to line eShop's own record up
/// against PayPal's for reconciliation.
/// </summary>
public class OrdersWithPaymentByDateRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentByDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null && o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
