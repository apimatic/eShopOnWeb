using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A saved card scoped to its owning shopper (one shopper never sees another's).</summary>
public class PaymentMethodByIdAndBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByIdAndBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
