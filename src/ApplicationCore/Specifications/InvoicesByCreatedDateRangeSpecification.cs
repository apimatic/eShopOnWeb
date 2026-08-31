using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShopOnWeb's own record of bills raised within a created-date range, for reconciliation.</summary>
public class InvoicesByCreatedDateRangeSpecification : Specification<Invoice>
{
    public InvoicesByCreatedDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(invoice => invoice.CreatedDate >= from && invoice.CreatedDate <= to);
    }
}
