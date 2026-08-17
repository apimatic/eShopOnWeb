using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single saved card, scoped to its owner so one shopper cannot reach another's card.</summary>
public sealed class SavedPaymentMethodByIdForBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
