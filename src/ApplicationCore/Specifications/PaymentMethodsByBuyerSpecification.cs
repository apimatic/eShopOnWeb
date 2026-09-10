using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's saved cards, newest first.</summary>
public sealed class PaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(pm => pm.BuyerId == buyerId)
            .OrderByDescending(pm => pm.CreatedAt);
    }

    public PaymentMethodsByBuyerSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(pm => pm.BuyerId == buyerId && pm.Id == paymentMethodId);
    }
}
