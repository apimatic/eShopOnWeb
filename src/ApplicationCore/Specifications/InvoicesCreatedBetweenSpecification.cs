using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every invoice eShop believes it raised within a time range, across all shoppers.
/// Used by the operator reconciliation report as the "what eShop believes" side.
/// </summary>
public class InvoicesCreatedBetweenSpecification : Specification<Invoice>
{
    public InvoicesCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(i => i.CreatedAt >= from && i.CreatedAt <= to)
            .Include(i => i.Items);
    }
}
