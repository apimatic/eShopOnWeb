using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop's own local record of bills created within a date range (for reconciliation).</summary>
public class InvoicesCreatedBetweenSpecification : Specification<Invoice>
{
    public InvoicesCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(i => i.CreatedDate >= from && i.CreatedDate <= to);
    }
}
