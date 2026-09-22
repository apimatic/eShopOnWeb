using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Order payments whose money actually moved (were captured) within a window, filtered on the
/// capture time — the same money-movement clock PayPal's transaction search filters on — not a
/// row-creation column.
/// </summary>
public class OrderPaymentsInWindowSpecification : Specification<OrderPayment>
{
    public OrderPaymentsInWindowSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to)
            .Include(p => p.Refunds);
    }
}
