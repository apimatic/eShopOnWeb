using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsInDateRangeSpec : Specification<Payment>
{
    public PaymentsInDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => (p.CreatedAt >= from && p.CreatedAt <= to)
                        || (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to))
            .Include(p => p.Refunds);
    }
}
