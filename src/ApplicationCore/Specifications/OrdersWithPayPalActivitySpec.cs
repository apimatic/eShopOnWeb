using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPayPalActivitySpec : Specification<Order>
{
    public OrdersWithPayPalActivitySpec()
    {
        Query
            .Where(o => o.Payment.PayPalOrderId != null || o.Payment.CaptureId != null)
            .Include(o => o.Refunds)
            .Include(o => o.Payment)
            .Include(o => o.OrderItems);
    }
}
