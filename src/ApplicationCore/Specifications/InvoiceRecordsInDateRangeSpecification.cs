using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop's own bills raised (created) within a date range — the eShop side of reconciliation.</summary>
public class InvoiceRecordsInDateRangeSpecification : Specification<InvoiceRecord>
{
    public InvoiceRecordsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(r => r.CreatedAt >= from && r.CreatedAt <= to);
    }
}
