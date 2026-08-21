using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentActivitySpec : Specification<Order>
{
    public OrdersWithPaymentActivitySpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o =>
                o.PayPalOrderId != null ||
                o.PayPalAuthorizationId != null ||
                o.PayPalCaptureId != null)
            .Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.AuthorizationTime != null && o.AuthorizationTime >= from && o.AuthorizationTime <= to))
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems);
    }
}
