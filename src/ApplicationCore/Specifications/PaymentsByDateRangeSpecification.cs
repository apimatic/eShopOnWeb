using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every payment whose order falls in a date range, for reconciliation. Payments have no date of
/// their own, so the range is applied to the owning order's date via the caller (the reconciliation
/// service joins to orders); this spec simply loads all payments with their refunds.
/// </summary>
public class AllPaymentsSpecification : Specification<Payment>
{
    public AllPaymentsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
