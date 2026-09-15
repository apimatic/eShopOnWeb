using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.PayPalAuthorizationCreated != null && o.PayPalAuthorizationCreated >= from && o.PayPalAuthorizationCreated <= to))
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}
