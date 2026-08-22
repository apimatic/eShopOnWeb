using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentActivitySpecification : Specification<Order>
{
    public OrdersWithPaymentActivitySpecification()
    {
        Query
            .Where(o => o.PayPalOrderId != null || o.PayPalAuthorizationId != null || o.PayPalCaptureId != null)
            .Include(o => o.Refunds);
    }

    public OrdersWithPaymentActivitySpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to)
                || (o.OriginalAuthorizedAt != null && o.OriginalAuthorizedAt >= from && o.OriginalAuthorizedAt <= to)
                || (o.CapturedAt != null && o.CapturedAt >= from && o.CapturedAt <= to)
                || o.PayPalOrderId != null
                || o.PayPalAuthorizationId != null
                || o.PayPalCaptureId != null)
            .Include(o => o.Refunds);
    }
}
