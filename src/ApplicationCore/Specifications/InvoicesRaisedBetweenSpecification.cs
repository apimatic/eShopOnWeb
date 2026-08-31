using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop's own bills raised within a (closed) date-time range, for reconciliation.</summary>
public class InvoicesRaisedBetweenSpecification : Specification<Invoice>
{
    public InvoicesRaisedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(i => i.RaisedAt >= from && i.RaisedAt <= to)
            .OrderBy(i => i.RaisedAt);
    }
}
