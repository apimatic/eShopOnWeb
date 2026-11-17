using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class PaymentsWithRefundsSpecification : Specification<Payment>
{
    public PaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
