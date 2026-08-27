using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsCreatedInRangeSpec : Specification<Payment>
{
    public PaymentsCreatedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
