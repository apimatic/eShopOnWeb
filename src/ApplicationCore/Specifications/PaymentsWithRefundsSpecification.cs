using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsWithRefundsSpecification : Specification<Payment>
{
    public PaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}

public class PaymentsByBuyerIdSpecification : Specification<Payment>
{
    public PaymentsByBuyerIdSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
