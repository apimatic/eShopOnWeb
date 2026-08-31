using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every eShop bill raised within a date range, across all shoppers (an operator view).</summary>
public class InvoicesCreatedBetweenSpecification : Specification<Invoice>
{
    public InvoicesCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(invoice => invoice.CreatedAt >= from && invoice.CreatedAt <= to)
            .OrderByDescending(invoice => invoice.CreatedAt);
    }
}
