using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All payments created within a date range, used to line eShop's own record up against
/// PayPal's transaction report during reconciliation.
/// </summary>
public class PaymentsInDateRangeSpecification : Specification<Payment>
{
    public PaymentsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
