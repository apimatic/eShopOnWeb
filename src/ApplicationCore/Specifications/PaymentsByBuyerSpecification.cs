using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments belonging to a buyer, with refunds loaded.</summary>
public class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
