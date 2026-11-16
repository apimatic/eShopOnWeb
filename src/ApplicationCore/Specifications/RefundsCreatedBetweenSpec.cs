using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class RefundsCreatedBetweenSpec : Specification<Refund>
{
    public RefundsCreatedBetweenSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(r => r.CreatedAt >= from && r.CreatedAt <= to)
            .OrderBy(r => r.CreatedAt);
    }
}
