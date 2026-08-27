using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders that carry any PayPal state, optionally bounded to an order-date range.
/// Used by reconciliation to line eShop payments up against PayPal's own records.
/// </summary>
public class OrdersWithPaymentInRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.PayPalOrderId != null ||
                         (o.OrderDate >= from && o.OrderDate <= to))
            .Include(o => o.Refunds);
    }
}
