using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoicesCreatedBetweenSpecification : Specification<Invoice>
{
    public InvoicesCreatedBetweenSpecification(DateTimeOffset fromInclusive, DateTimeOffset toInclusive)
    {
        Query.Where(invoice => invoice.CreatedAt >= fromInclusive && invoice.CreatedAt <= toInclusive);
    }
}
