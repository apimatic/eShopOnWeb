using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentActivitySpec : Specification<Order>
{
    public OrdersWithPaymentActivitySpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds)
            .Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.PayPalAuthorizationCreated != null && o.PayPalAuthorizationCreated >= from && o.PayPalAuthorizationCreated <= to) ||
                o.Refunds.Count > 0);
    }
}

public class AllOrdersWithPaymentSpec : Specification<Order>
{
    public AllOrdersWithPaymentSpec()
    {
        Query
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds)
            .Where(o => o.PayPalOrderId != null || o.PayPalAuthorizationId != null || o.PayPalCaptureId != null);
    }
}
