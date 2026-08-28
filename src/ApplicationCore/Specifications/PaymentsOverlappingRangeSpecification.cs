using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments whose life overlapped the reporting window — created on or before the window closes and
/// last touched on or after it opens. A payment authorized just before the window and captured
/// inside it belongs in the report, so matching on creation time alone would miss it.
/// </summary>
public class PaymentsOverlappingRangeSpecification : Specification<Payment>
{
    public PaymentsOverlappingRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt <= to && p.UpdatedAt >= from)
            .Include(p => p.Refunds);
    }
}
