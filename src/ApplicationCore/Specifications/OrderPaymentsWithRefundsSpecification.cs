using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsWithRefundsSpecification : Specification<OrderPayment>
{
    public OrderPaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
