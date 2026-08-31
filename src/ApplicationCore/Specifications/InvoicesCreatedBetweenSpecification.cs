using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every eShop bill raised (created) within a date range — the eShop side of reconciliation.</summary>
public class InvoicesCreatedBetweenSpecification : Specification<Invoice>
{
    public InvoicesCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(i => i.CreatedDate >= from && i.CreatedDate <= to);
    }
}
