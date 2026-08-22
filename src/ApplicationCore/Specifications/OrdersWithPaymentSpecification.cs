using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentInRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.PayPalOrderId != null && o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}

public class OrdersWithPayPalIdsSpecification : Specification<Order>
{
    public OrdersWithPayPalIdsSpecification()
    {
        Query.Where(o => o.PayPalOrderId != null || o.PayPalCaptureId != null || o.PayPalAuthorizationId != null)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}
