using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop's own bills raised (created) within an inclusive date-time window.</summary>
public sealed class InvoicesRaisedBetweenSpecification : Specification<Invoice>
{
    public InvoicesRaisedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(i => i.CreatedAt >= from && i.CreatedAt <= to)
            .OrderBy(i => i.CreatedAt);
    }
}
