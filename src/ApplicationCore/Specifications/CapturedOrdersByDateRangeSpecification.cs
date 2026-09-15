using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders whose payment was captured within [from, to] — the eShop side of reconciliation
/// (only captured payments move money that PayPal's transaction record would show).
/// </summary>
public class CapturedOrdersByDateRangeSpecification : Specification<Order>
{
    public CapturedOrdersByDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null
                        && o.Payment.CapturedAt != null
                        && o.Payment.CapturedAt >= from
                        && o.Payment.CapturedAt <= to)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
