using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class PaymentsByBuyerIdSpec : Specification<Payment>
{
    public PaymentsByBuyerIdSpec(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.OrderId);
    }
}
