using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads orders whose payment was captured within the given range (the eShop side of reconciliation).</summary>
public class OrdersCapturedBetweenSpecification : Specification<Order>
{
    public OrdersCapturedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null
                        && o.Payment.CapturedAt != null
                        && o.Payment.CapturedAt >= from
                        && o.Payment.CapturedAt <= to);
        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
