using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersForReconciliationSpecification : Specification<Order>
{
    public OrdersForReconciliationSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o =>
                (o.AuthorizedAt != null && o.AuthorizedAt >= from && o.AuthorizedAt <= to)
                || (o.FulfilledAt != null && o.FulfilledAt >= from && o.FulfilledAt <= to)
                || (o.CancelledAt != null && o.CancelledAt >= from && o.CancelledAt <= to)
                || o.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)
                || (o.OrderDate >= from && o.OrderDate <= to && o.PayPalOrderId != null))
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems);
    }
}
