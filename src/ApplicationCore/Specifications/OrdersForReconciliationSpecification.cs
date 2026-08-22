using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersForReconciliationSpecification : Specification<Order>
{
    public OrdersForReconciliationSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems)
            .Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.Payment.AuthorizationCreatedAt != null &&
                    o.Payment.AuthorizationCreatedAt >= from &&
                    o.Payment.AuthorizationCreatedAt <= to) ||
                (o.Payment.CapturedAt != null &&
                    o.Payment.CapturedAt >= from &&
                    o.Payment.CapturedAt <= to) ||
                o.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to));
    }
}
