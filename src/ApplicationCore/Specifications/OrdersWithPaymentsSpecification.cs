using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentsSpecification : Specification<Order>
{
    public OrdersWithPaymentsSpecification()
    {
        Query
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .Where(o => o.PayPalOrderId != null || o.PayPalAuthorizationId != null || o.PayPalCaptureId != null);
    }
}

public class OrdersByDateRangeSpecification : Specification<Order>
{
    public OrdersByDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .Where(o => o.OrderDate >= from && o.OrderDate <= to);
    }
}
