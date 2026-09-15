using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads orders that have a captured payment within a date range, for reconciliation against PayPal's
/// own transaction record. Ordered by the payment's creation time.
/// </summary>
public class CapturedOrdersByDateRangeSpecification : Specification<Order>
{
    public CapturedOrdersByDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null
                        && o.Payment.CaptureId != null
                        && o.Payment.CreatedAt >= from
                        && o.Payment.CreatedAt <= to);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
