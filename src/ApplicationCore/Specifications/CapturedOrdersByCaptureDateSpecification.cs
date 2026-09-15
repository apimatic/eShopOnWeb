using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders whose payment was captured within a date range — the eShop side of reconciliation.
/// </summary>
public class CapturedOrdersByCaptureDateSpecification : Specification<Order>
{
    public CapturedOrdersByCaptureDateSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null
                        && o.Payment.CaptureId != null
                        && o.Payment.CapturedAt != null
                        && o.Payment.CapturedAt >= from
                        && o.Payment.CapturedAt <= to);

        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
