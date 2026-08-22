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
            .Include(o => o.Refunds)
            .Where(o => o.PayPalOrderId != null || o.PayPalCaptureId != null || o.PayPalAuthorizationId != null);
    }
}
