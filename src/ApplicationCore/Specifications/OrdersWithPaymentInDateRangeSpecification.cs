using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders whose payment authorization was created within [from, to] - the candidate set for
/// reconciling eShop's own payment records against PayPal's transaction search report.
/// </summary>
public class OrdersWithPaymentInDateRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.Payment != null
                && o.Payment.AuthorizationCreatedAt >= from
                && o.Payment.AuthorizationCreatedAt <= to)
            .Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
