using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsWithRefundsSpec : Specification<OrderPayment>
{
    public OrderPaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
