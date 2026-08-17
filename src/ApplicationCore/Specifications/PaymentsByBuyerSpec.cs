using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class PaymentsByBuyerSpec : Specification<Payment>
{
    public PaymentsByBuyerSpec(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
