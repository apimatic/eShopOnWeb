using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop's own record of the bills it believes it raised within a date range.</summary>
public sealed class InvoicesCreatedBetweenSpecification : Specification<Invoice>
{
    public InvoicesCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(i => i.CreatedDate >= from && i.CreatedDate <= to)
            .OrderBy(i => i.CreatedDate);
    }
}
