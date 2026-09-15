using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentInDateRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .Where(o => o.Payment != null &&
                        ((o.Payment.CapturedAt != null && o.Payment.CapturedAt >= from && o.Payment.CapturedAt <= to) ||
                         (o.Payment.AuthorizationCreatedAt != null && o.Payment.AuthorizationCreatedAt >= from && o.Payment.AuthorizationCreatedAt <= to) ||
                         (o.OrderDate >= from && o.OrderDate <= to)));
    }
}
