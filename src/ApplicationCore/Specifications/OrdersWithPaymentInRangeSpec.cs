using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders that carry a payment (are past AwaitingPayment) and were placed within the date range —
/// the eShop side of reconciliation, across all buyers (operator scope).
/// </summary>
public class OrdersWithPaymentInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.Status != OrderStatus.AwaitingPayment
                         && o.OrderDate >= from && o.OrderDate <= to);
    }
}
