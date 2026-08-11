using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a buyer's payments (with refunds), for building the my-orders view.</summary>
public class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
