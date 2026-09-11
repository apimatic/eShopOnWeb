using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsInDateRangeSpecification : Specification<Payment>
{
    public PaymentsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedDate >= from && p.CreatedDate <= to)
            .Include(p => p.Refunds);
    }
}
